using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Winter;

namespace WinterExtensions
{
    class BettingSystem
    {
        enum State
        {
            None,
            Open,
            WaitingResult
        }

        State m_state = State.None;
        WinterBot m_bot;


        HashSet<string> m_betting;
        Dictionary<TwitchUser, Tuple<string, int>> m_bets;
        int m_time;
        DateTime m_bettingStarted;
        DateTime m_lastMessage;
        bool m_callback;
        string m_url, m_channel;

        Dictionary<TwitchUser, int> m_points = new Dictionary<TwitchUser, int>();

        object m_sync = new object();
        List<Tuple<TwitchUser, int>> m_queue = new List<Tuple<TwitchUser, int>>();
        AutoResetEvent m_event = new AutoResetEvent(false);
        volatile bool m_shutdown;
        Thread m_thread;


        public BettingSystem(WinterBot bot)
        {
            m_bot = bot;
            m_channel = m_bot.Channel.ToLower();
            Enabled = true;

            var section = m_bot.Options.IniReader.GetSectionByName("chat");
            if (section == null || !section.GetValue("httplogger", ref m_url))
                return;
        }

        bool IsBettingClosed { get { return m_state == State.None; } }
        bool IsBettingOpen { get { return m_state == State.Open; } }
        bool WaitingResult { get { return m_state == State.WaitingResult; } }

        public bool Enabled { get; private set; }
        
        [BotCommand(AccessLevel.Mod, "betting")]
        public void BettingEnable(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            bool enable = false;
            if (!value.ParseBool(ref enable))
            {
                sender.SendMessage("Betting is currently: {0}", Enabled ? "enabled" : "disabled");
                return;
            }

            Enabled = enable;
            sender.SendMessage("Betting is now {0}.", Enabled ? "enabled" : "disabled");
        }


        [BotCommand(AccessLevel.Mod, "openbetting", "openbet", "startbet", "startbetting")]
        public void OpenBetting(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!Enabled)
                return;

            if (IsBettingOpen || WaitingResult)
            {
                sender.SendResponse("Betting is currently ongoing.  Use !result to award points, use !cancelbet to cancel the current bet.");
                return;
            }

            HashSet<string> betting = new HashSet<string>();
            Args args = value.ToLower().ParseArguments(sender);

            int time = args.GetIntFlag("time", 120, false);
            string val;
            while ((val = args.GetOneWord()) != null)
                betting.Add(val);

            if (betting.Count < 2)
            {
                sender.SendResponse("Need at least two people to bet on!");
                return;
            }

            m_betting = betting;
            m_bets = new Dictionary<TwitchUser, Tuple<string, int>>();
            m_state = State.Open;
            m_time = time;
            m_bettingStarted = DateTime.Now;

            GetCallback();

            sender.SendResponse("Betting is now OPEN.  Use '!bet [player] [amount]' to bet.  Minimum bet is 1, maximum bet is 500.  You start with 3000 points, you can bet even if you have no points.");
        }

        private void GetCallback()
        {
            if (!m_callback)
                m_bot.Tick += m_bot_Tick;

            m_callback = true;
        }

        private void CancelCallback()
        {
            if (m_callback)
                m_bot.Tick -= m_bot_Tick;

            m_callback = false;
        }

        
        [BotCommand(AccessLevel.Normal, "bet")]
        public void Bet(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!Enabled)
                return;

            if (!IsBettingOpen)
            {
                SendMessage(sender, "{0}: Betting is not currently open.", user.Name);
                return;
            }

            const int min = 1;
            const int max = 500;

            Args args = value.ToLower().ParseArguments(sender);

            string who;
            int amount;
            if (args.GetInt(out amount))
            {
                who = args.GetOneWord();
            }
            else
            {
                args.Reset();
                who = args.GetOneWord();
                amount = args.GetInt();
            }

            if (args.Error != null || string.IsNullOrWhiteSpace(who) || !m_betting.Contains(who))
                return;

            if (amount < min)
                amount = min;
            else if (amount > max)
                amount = max;

            m_bets[user] = new Tuple<string, int>(who, amount);
            if (m_thread == null)
            {
                m_thread = new Thread(SaveProc);
                m_thread.Start();

                m_bot.BeginShutdown += m_bot_BeginShutdown;
                m_bot.EndShutdown += m_bot_EndShutdown;
            }
        }

        
        [BotCommand(AccessLevel.Mod, "cancelbet")]
        public void CancelBet(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!Enabled)
                return;

            if (IsBettingClosed)
                return;

            ClearBetting();
            CancelCallback();
        }

        [BotCommand(AccessLevel.Mod, "result")]
        public void Result(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!Enabled)
                return;

            if (IsBettingClosed)
            {
                sender.SendMessage("Betting is not currently open.");
                return;
            }

            Args args = value.ParseArguments(sender);
            string result = args.GetOneWord();

            if (string.IsNullOrWhiteSpace(result))
            {
                sender.SendMessage("Usage: '!result [player]'.");
                return;
            }
            else if (!m_betting.Contains(result))
            {
                sender.SendMessage("'{0}' not a valid player, valid players: {1}", result, string.Join(", ", m_betting));
                return;
            }

            var winners = from bet in m_bets
                          where bet.Value.Item1.Equals(result, StringComparison.CurrentCultureIgnoreCase)
                          select bet;

            var losers = from bet in m_bets
                         where !bet.Value.Item1.Equals(result, StringComparison.CurrentCultureIgnoreCase)
                         select bet;


            int totalWinners = 0, totalLosers = 0;
            foreach (var winner in winners)
            {
                AddPoints(winner.Key, winner.Value.Item2);
                totalWinners++;
            }

            foreach (var loser in losers)
            {
                AddPoints(loser.Key, -loser.Value.Item2);
                totalLosers++;
            }


            Tuple<TwitchUser, int>[] t = (from bet in m_bets
                                               select new Tuple<TwitchUser, int>(bet.Key, m_points[bet.Key])).ToArray();


            lock (m_sync)
                foreach (var usr in m_bets.Keys)
                    m_queue.Add(new Tuple<TwitchUser, int>(usr, m_points[usr]));

            m_event.Set();


            ClearBetting();
            CancelCallback();

            sender.SendMessage("Betting complete.  {0} winners and {1} losers.", totalWinners, totalLosers);
        }

        private void AddPoints(TwitchUser user, int amount)
        {
            int curr;
            if (!m_points.TryGetValue(user, out curr))
                curr = 3000;

            m_points[user] = curr + amount;
        }
        


        void m_bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            if (!IsBettingOpen)
            {
                CancelCallback();
                return;
            }

            if (m_bettingStarted.Elapsed().TotalSeconds >= m_time)
            {
                sender.SendMessage("Betting is now CLOSED.");
                CancelCallback();
                m_state = State.WaitingResult;
            }
        }

        void m_bot_EndShutdown(WinterBot sender)
        {
            m_thread.Join();
        }

        void m_bot_BeginShutdown(WinterBot sender)
        {
            m_shutdown = true;
            m_event.Set();
        }

        private void SaveProc()
        {
            while (!m_shutdown)
            {
                m_event.WaitOne(15000);

                List<Tuple<TwitchUser, int>> queue;
                lock (m_sync)
                {
                    if (m_queue.Count == 0)
                        continue;

                    queue = m_queue;
                    m_queue = new List<Tuple<TwitchUser, int>>();
                }

                if (!Save(queue))
                {
                    lock (m_sync)
                    {
                        var tmp = m_queue;
                        m_queue = queue;

                        if (m_queue.Count != 0)
                            m_queue.AddRange(tmp);
                    }
                }
            }
        }

        private bool Save(List<Tuple<TwitchUser, int>> queue)
        {
            bool succeeded = false;
            DateTime now = DateTime.Now;
            string url = string.Format("{0}/addpoints.php?CHANNEL={1}", m_url, m_channel);

            try
            {
                WebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/x-gzip";

                Stream requestStream = request.GetRequestStream();
                using (GZipStream gzip = new GZipStream(requestStream, CompressionLevel.Optimal))
                using (StreamWriter stream = new StreamWriter(gzip))
                    foreach (var item in queue)
                        stream.Write("{0}\n{1}\n", item.Item1.Name, item.Item2);

                string result;
                WebResponse response = request.GetResponse();
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                    result = reader.ReadToEnd();

                succeeded = result == "ok";
            }
            catch (Exception e)
            {
                m_bot.WriteDiagnostic(DiagnosticFacility.Error, "Failed to save points.");
            }

            return succeeded;
        }


        void ClearBetting()
        {
            m_betting = null;
            m_bets = null;
            m_state = State.None;
            m_time = 0;
        }



        private void SendMessage(WinterBot sender, string fmt, params object[] args)
        {
            if (m_lastMessage.Elapsed().Seconds < 20)
                return;

            sender.SendMessage(string.Format(fmt, args));
            m_lastMessage = DateTime.Now;
        }
    }



    class BettingOptions
    {
        bool m_enabled;

        public bool Enabled { get { return m_enabled; } set { m_enabled = value; } }


        public BettingOptions(Options options)
        {
            var ini = options.IniReader;
            var section = ini.GetSectionByName("betting");
            if (section != null)
                section.GetValue("Enabled", ref m_enabled);
        }
    }

    class PointTable : SavableDictionary<string, int>
    {
        public PointTable(WinterBot bot)
            :base(bot, "points")
        {
        }

        protected override IEnumerable<Tuple<string, int>> Deserialize(IEnumerable<string> lines)
        {
            char[] splitArg = new char[] { ' ' };

            foreach (var line in lines)
            {
                string[] s = line.ToLower().Trim().Split(new char[]{' '}, 2);
                if (s.Length != 2)
                    continue;

                int amount;
                if (!int.TryParse(s[1], out amount))
                    continue;

                yield return new Tuple<string, int>(s[0].ToLower(), amount);
            }
        }

        protected override IEnumerable<string> Serialize(IEnumerable<Tuple<string, int>> values)
        {
            foreach (var value in values)
                yield return string.Format("{0} {1}", value.Item1, value.Item2);
        }
    }


    class Betting
    {
        PointTable m_points;
        HashSet<TwitchUser> m_pointsRequest = new HashSet<TwitchUser>();
        Dictionary<TwitchUser, Tuple<string, int>> m_confirmRequest = new Dictionary<TwitchUser, Tuple<string, int>>();

        DateTime m_lastMessage = DateTime.Now;
        DateTime m_lastOpenUpdate = DateTime.Now;
        BettingRound m_currentRound, m_lastRound;
        StringQueue m_toSave;

        WinterBot m_bot;
        object m_sync = new object();

        BettingOptions m_options;

        public Betting(WinterBot bot)
        {
            m_bot = bot;
            m_options = new BettingOptions(bot.Options);
            
            m_toSave = new StringQueue(bot, "pointlog");
            m_points = new PointTable(bot);

            bot.BeginShutdown += bot_BeginShutdown;

            if (m_options.Enabled)
                Enable();
        }

        void bot_BeginShutdown(WinterBot sender)
        {
            SaveLastRound();
        }

        void Enable()
        {
            m_bot.Tick += bot_Tick;
        }

        void Disable()
        {
            if (m_lastRound != null)
                SaveLastRound();

            m_bot.Tick -= bot_Tick;
        }

        bool IsBettingOpen { get { return m_currentRound != null && m_currentRound.Open; } }

        bool WaitingResult { get { return m_currentRound != null && m_currentRound.Result == null; } }

        [BotCommand(AccessLevel.Mod, "betting")]
        public void BettingMode(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                sender.SendResponse("Betting is currently {0}.", m_options.Enabled ? "enabled" : "disabled");
                return;
            }

            bool mode = false;
            if (!value.ParseBool(ref mode))
            {
                sender.SendResponse("Usage: {0} [enable|disable]", cmd);
                return;
            }

            if (m_options.Enabled == mode)
            {
                sender.SendResponse("Betting is currently {0}.", m_options.Enabled ? "enabled" : "disabled");
                return;
            }
            else
            {
                m_options.Enabled = mode;
                sender.SendResponse("Betting is now {0}.", m_options.Enabled ? "enabled" : "disabled");
            }


            if (m_options.Enabled)
                Enable();
            else
                Disable();
        }

        [BotCommand(AccessLevel.Mod, "openbetting", "openbet", "startbet", "startbetting")]
        public void OpenBetting(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!m_options.Enabled)
                return;

            if (IsBettingOpen || WaitingResult)
            {
                sender.SendResponse("Betting is currently ongoing.  Use !result to award points, use !cancelbet to cancel the current bet.");
                return;
            }

            bool confirm;
            int time;
            HashSet<string> values = ParseOpenBet(sender, value, out confirm, out time);
            if (values == null)
                return;

            if (values.Count < 2)
            {
                sender.SendResponse("Usage: '!openbetting option1 option2'.");
                return;
            }

            m_currentRound = new BettingRound(this, user, values, confirm, time);
            WriteOpenBetMessage(sender, true);
        }

        [BotCommand(AccessLevel.Mod, "cancelbetting", "cancelbet")]
        public void CancelBetting(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!m_options.Enabled)
                return;

            if (m_currentRound == null)
            {
                sender.SendResponse("Betting is not currently open.");
                return;
            }

            CancelBetting(sender);
        }

        [BotCommand(AccessLevel.Mod, "addpoints")]
        public void AddPoints(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!m_options.Enabled)
                return;

            TwitchUser who;
            int points;
            if (!ParsePoints(sender, user, cmd, value, out who, out points))
                return;

            if (points == 0)
                return;

            AddPoints(who, points);
            PointsMessage(sender, user, who, points);
        }

        private void PointsMessage(WinterBot sender, TwitchUser user, TwitchUser who, int points)
        {
            if (points > 0)
                sender.SendResponse("{0}: Gave {1} points to {2}.  {2} now has {3} points.", user.Name, points, who.Name, GetPoints(who));
            else
                sender.SendResponse("{0}: Took {1} points from {2}.  {2} now has {3} points.", user.Name, -points, who.Name, GetPoints(who));
        }

        [BotCommand(AccessLevel.Mod, "removepoints", "subpoints", "subtractpoints")]
        public void RemovePoints(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!m_options.Enabled)
                return;

            TwitchUser who;
            int points;
            if (!ParsePoints(sender, user, cmd, value, out who, out points))
                return;

            if (points == 0)
                return;

            if (points > 0)
                points = -points;

            AddPoints(who, points);
            PointsMessage(sender, user, who, points);
        }

        [BotCommand(AccessLevel.Normal, "bet")]
        public void BetCommand(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!m_options.Enabled)
                return;

            if (!IsBettingOpen)
            {
                SendMessage(sender, "{0}: Betting is not currently open.", user.Name);
                return;
            }

            string who;
            int bet;

            if (!ParseBet(sender, user, value, out who, out bet))
                return;

            m_currentRound.PlaceBet(user, who, bet);

            if (m_currentRound.Confirm)
                m_confirmRequest[user] = new Tuple<string, int>(who, bet);
        }

        [BotCommand(AccessLevel.Normal, "points")]
        public void PointsCommand(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!m_options.Enabled)
                return;

            m_pointsRequest.Add(user);
        }
        
        [BotCommand(AccessLevel.Mod, "result", "winner")]
        public void ResultCommand(WinterBot sender, TwitchUser user, string cmd, string result)
        {
            if (!m_options.Enabled)
                return;

            var round = m_currentRound != null ? m_currentRound : m_lastRound;
            if (round == null)
            {
                sender.SendResponse("Not currently waiting for results.");
                return;
            }

            result = result.Trim().ToLower();
            if (!round.Values.Contains(result))
            {
                sender.SendResponse("{0}: '{1}' is not a voting option, options are: {2}", user.Name, result, string.Join(", ", round.Values));
                return;
            }

            string oldResult = round.Result;
            if (round.Result == result)
                return;

            round.Open = false;
            round.ReportResult(user, result);

            if (oldResult == null)
            {
                if (round.Winners > 0 || round.Losers > 0)
                    sender.SendResponse("Bet complete, results are tallied.  {0} people won, {1} people lost.", round.Winners, round.Losers);
                else
                    sender.SendResponse("Bet complete (no bets).");
            }
            else
            {
                sender.SendResponse("Rolled back betting result '{0}', new result '{1}'.", oldResult, result);
            }

            lock (m_sync)
            {
                if (m_currentRound != null)
                {
                    if (m_lastRound != null)
                        m_toSave.Add(m_lastRound);

                    m_lastRound = m_currentRound;
                    m_currentRound = null;
                }
            }
        }


        DateTime m_lastPointMessage = DateTime.Now;
        void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            if (IsBettingOpen)
            {
                if (m_currentRound.OpenTime.Elapsed().TotalSeconds >= m_currentRound.Time)
                {
                    m_currentRound.Open = false;
                    sender.SendResponse("Betting is now closed.");
                }
                else if (m_currentRound.Time > 60 && m_lastOpenUpdate.Elapsed().TotalSeconds > 60)
                {
                    WriteOpenBetMessage(sender);
                }
            }

            if (m_lastPointMessage.Elapsed().TotalSeconds >= 10)
            {
                m_lastPointMessage = DateTime.Now;
                if (m_confirmRequest.Count > 0)
                {
                    if (m_confirmRequest.Count == 1)
                    {
                        var item = m_confirmRequest.First();
                        sender.SendResponse("{0}: Bet {1} points for {2}.", item.Key.Name, item.Value.Item2, item.Value.Item1);
                        m_confirmRequest.Clear();
                    }
                    else
                    {
                        StringBuilder sb = new StringBuilder(300);
                        List<TwitchUser> users = new List<TwitchUser>(32);

                        bool first = true;

                        foreach (var item in m_confirmRequest)
                        {
                            if (sb.Length > 255)
                                break;

                            if (!first)
                                sb.Append(", ");

                            sb.AppendFormat("{0} bet {1} points for {2}", item.Key.Name, item.Value.Item2, item.Value.Item1);
                        }

                        sb.Append('.');
                        sender.SendResponse(sb.ToString());

                        if (users.Count == m_confirmRequest.Count)
                        {
                            m_confirmRequest.Clear();
                        }
                        else
                        {
                            foreach (var user in users)
                                m_confirmRequest.Remove(user);
                        }
                    }

                    m_lastMessage = DateTime.Now;
                }
                else if (m_pointsRequest.Count > 0)
                {
                    if (m_pointsRequest.Count == 1)
                    {
                        TwitchUser user = m_pointsRequest.First();
                        int points = GetPoints(user);

                        sender.SendResponse("{0}: You have {1} points.", user, points);
                    }
                    else
                    {
                        sender.SendResponse("Point totals: " + string.Join(", ", from user in m_pointsRequest
                                                                                let name = user.Name
                                                                                let points = GetPoints(user)
                                                                                select string.Format("{0} has {1} points", name, points)));
                    }

                    m_pointsRequest.Clear();
                    m_lastMessage = DateTime.Now;
                }
            }

            if (m_lastRound != null && m_lastRound.CloseTime.Elapsed().TotalMinutes >= 5)
            {
                SaveLastRound();
            }
        }


        private void SaveLastRound()
        {
            lock (m_sync)
            {
                if (m_lastRound != null)
                {
                    m_toSave.Add(m_lastRound);
                    m_lastRound = null;
                }
            }
        }


        internal void AddPoints(TwitchUser user, int points)
        {
            string name = user.Name.ToLower();
            m_points[name] = GetPoints(user) + points;
        }

        private int GetPoints(TwitchUser user)
        {
            int curr;
            if (!m_points.TryGetValue(user.Name, out curr))
                curr = user.IsSubscriber ? 4000 : 3000;
            return curr;
        }

        private void SendMessage(WinterBot bot, string fmt, params object[] args)
        {
            if (m_lastMessage.Elapsed().TotalSeconds >= 10)
            {
                bot.SendResponse(fmt, args);
                m_lastMessage = DateTime.Now;
            }
        }


        HashSet<string> ParseOpenBet(WinterBot bot, string value, out bool confirm, out int time)
        {
            confirm = false;
            time = 60;

            HashSet<string> result = new HashSet<string>();
            value = value.ToLower();
            foreach (string item in value.Split(' ', ','))
            {
                if (item.StartsWith("-"))
                {
                    if (item == "-confirm")
                    {
                        confirm = true;
                    }
                    else if (item.StartsWith("-time="))
                    {
                        string timeStr = item.Substring(6);
                        if (!int.TryParse(timeStr, out time))
                        {
                            bot.SendResponse("Usage: '!openbetting -time=[seconds] option1 option2'.  Minimum of 30 seconds, maximum of 600 seconds.");
                            return null;
                        }
                    }
                    else
                    {
                        bot.SendResponse("!openbetting: unknown option '{0}'", item);
                        return null;
                    }
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(item))
                        result.Add(item);
                }
            }

            return result;
        }


        private bool ParsePoints(WinterBot bot, TwitchUser user, string cmd, string value, out TwitchUser who, out int points)
        {
            who = null;
            points = 0;

            value = value.ToLower();
            string[] args = value.Split(new char[] { ' ' }, 2);
            if (args.Length == 0 || args.Length != 2)
            {
                AddPointsUsage(bot, user, cmd);
                return false;
            }

            string name = args[0];
            if (!TwitchUsers.IsValidUserName(name))
            {
                AddPointsUsage(bot, user, cmd);
                return false;
            }

            who = bot.Users.GetUser(name);
            if (!int.TryParse(args[1], out points))
            {
                AddPointsUsage(bot, user, cmd);
                return false;
            }

            return true;
        }

        private static void AddPointsUsage(WinterBot bot, TwitchUser user, string cmd)
        {
            bot.SendResponse("{0}:  Usage:  {1} [who] [amount].", user.Name, cmd);
        }

        private bool ParseBet(WinterBot bot, TwitchUser user, string value, out string who, out int bet)
        {
            who = null;
            bet = 50;

            value = value.ToLower();
            string[] args = value.Split(new char[] { ' ' }, 2);
            if (args.Length == 0 || args.Length > 2)
            {
                SendMessage(bot, "{0}:  Usage:  !bet [who] [amount].  (Minimum bet is 1, maximum bet is 500.)", user.Name);
                return false;
            }

            who = args[0];
            if (!m_currentRound.IsValidOption(who))
            {
                SendMessage(bot, "{0}: {1} is not a valid option.  Options are: {2}.", user.Name, who, string.Join(", ", m_currentRound.Values));
                return false;
            }

            int totalPoints = GetPoints(user);
            int max = 500;
            if (totalPoints < max)
                max = totalPoints;
            
            if (max < 50)
                max = 50;

            if (args.Length == 2)
            {
                string betString = args[1].ToLower();
                if (!int.TryParse(args[1], out bet))
                {
                    if (betString == "min")
                    {
                        bet = 1;
                    }
                    else if (betString == "max")
                    {
                        bet = max;
                    }
                    else
                    {
                        SendMessage(bot, "{0}:  Usage:  !bet [who] [amount].  (Minimum bet is 1, maximum bet is 500.)", user.Name);
                        return false;
                    }
                }
            }

            if (bet <= 0)
                bet = 1;
            else if (bet > max)
                bet = max;

            return true;
        }

        private void CancelBetting(WinterBot sender)
        {
            if (m_currentRound != null)
            {
                sender.SendResponse("Betting is cancelled.");
                m_currentRound.Open = false;
                m_currentRound = null;
            }
        }
        

        private void WriteOpenBetMessage(WinterBot sender, bool first = false)
        {
            int time = first ? m_currentRound.Time : (int)(m_currentRound.Time - m_currentRound.OpenTime.Elapsed().TotalSeconds);
            sender.SendResponse("Betting is now open, use '!bet [player] [amount]' to bet.  Current players: {0}.  Max bet is 100 points (or up to 25 if you have none), betting closes in {1} seconds.", string.Join(", ", m_currentRound.Values), time);
            m_lastOpenUpdate = m_lastMessage = DateTime.Now;
        }
    }

    class BettingRound
    {
        Dictionary<string, Dictionary<TwitchUser, int>> m_bets = new Dictionary<string, Dictionary<TwitchUser, int>>();
        int m_winners, m_losers;
        private Betting m_betting;
        private TwitchUser m_createdBy;
        private TwitchUser m_closedBy;

        public IEnumerable<string> Values { get { return m_bets.Keys; } }

        public bool Confirm { get; set; }

        public int Time { get; set; }

        public string Result { get; private set; }

        public DateTime OpenTime { get; set; }

        public DateTime CloseTime { get; set; }

        public bool Open { get; set; }

        public int Winners { get { return m_winners; } }

        public int Losers { get { return m_losers; } }

        public BettingRound(Betting betting, TwitchUser createdBy, HashSet<string> values, bool confirm, int time)
        {
            Debug.Assert(values != null);
            Debug.Assert(values.Count >= 2);

            if (time < 30)
                time = 30;
            else if (time > 600)
                time = 600;

            m_betting = betting;
            Confirm = confirm;
            Time = time;
            OpenTime = DateTime.Now;
            Open = true;
            m_createdBy = createdBy;

            foreach (string value in values)
                m_bets[value.ToLower()] = new Dictionary<TwitchUser, int>();
        }

        public void PlaceBet(TwitchUser user, string option, int bet)
        {
            Debug.Assert(Values.Contains(option));

            RemoveBet(user);

            Dictionary<TwitchUser, int> bets;
            if (m_bets.TryGetValue(option, out bets))
                bets[user] = bet;
        }

        private void RemoveBet(TwitchUser user)
        {
            foreach (Dictionary<TwitchUser, int> bets in m_bets.Values)
                if (bets.ContainsKey(user))
                    bets.Remove(user);
        }

        public bool IsValidOption(string value)
        {
            value = value.Trim().ToLower();
            return m_bets.ContainsKey(value);
        }

        public void ReportResult(TwitchUser user, string result)
        {
            m_closedBy = user;
            result = result.ToLower();

            if (Result != null)
                UpdatePoints(Result, true);

            Result = result;
            UpdatePoints(result);
            CloseTime = DateTime.Now;
        }

        public void RollbackResult()
        {
            if (Result != null)
            {
                UpdatePoints(Result, true);
                Result = null;
            }
        }

        private void UpdatePoints(string result, bool rollback = false)
        {
            m_winners = 0;
            m_losers = 0;

            foreach (var item in m_bets)
            {
                if (item.Key == result)
                {
                    // Winners
                    foreach (var bet in item.Value)
                    {
                        if (!rollback)
                        {
                            m_betting.AddPoints(bet.Key, bet.Value);
                            m_winners++;
                        }
                        else
                        {
                            m_betting.AddPoints(bet.Key, -bet.Value);
                        }
                    }
                }
                else
                {
                    // Losers
                    foreach (var bet in item.Value)
                    {
                        if (!rollback)
                        {
                            m_betting.AddPoints(bet.Key, -bet.Value);
                            m_losers++;
                        }
                        else
                        {
                            m_betting.AddPoints(bet.Key, bet.Value);
                        }
                    }
                }
            }
        }
        

        public override string ToString()
        {
            Debug.Assert(!Open);
            Debug.Assert(Result != null);
            StringBuilder sb = new StringBuilder();

            sb.AppendFormat("Bet on: {0}.  Ran from {1} - {2}, with result {3}\n", string.Join(", ", Values), OpenTime, CloseTime, Result);
            sb.AppendFormat("Created by: {0}\n", m_createdBy);
            sb.AppendFormat("Closed by: {0}\n", m_closedBy);
            if (Result != null && m_bets.ContainsKey(Result))
                sb.AppendLine("Winners: " + string.Join(", ", from item in m_bets[Result]
                                                              select item.Key.Name + "=" + item.Value));

            sb.AppendLine("Losers:");
            foreach (var item in m_bets)
            {
                if (item.Key == Result)
                    continue;

                sb.Append(item.Key);
                sb.Append(": ");

                sb.AppendLine(string.Join(", ", from i2 in item.Value
                                                select i2.Key.Name + "=" + i2.Value));
            }

            sb.AppendLine();

            return sb.ToString();
        }
    }
}
