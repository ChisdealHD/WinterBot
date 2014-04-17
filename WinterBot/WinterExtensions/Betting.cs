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

            ThreadPool.QueueUserWorkItem(LoadPoints);
        }

        private void LoadPoints(object o)
        {
            DateTime now = DateTime.Now;
            string url = string.Format("{0}/getpoints.php?CHANNEL={1}", m_url, m_channel);

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "GET";
                request.ContentType = "application/x-gzip";
                request.KeepAlive = false;

                WebResponse response = request.GetResponse();
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        var arr = line.Split(' ');
                        if (arr.Length != 2)
                            continue;

                        int val;
                        if (!int.TryParse(arr[1], out val))
                            continue;

                        m_points[m_bot.Users.GetUser(arr[0])] = val;
                    }
                }
            }
            catch (Exception)
            {
                m_bot.WriteDiagnostic(DiagnosticFacility.Error, "Failed to save points.");
            }
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
            Console.WriteLine("BETTING CLOSED.");
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

            sender.SendMessage("Betting complete.  {0} winners and {1} losers.  Point totals can be found here: http://www.darkautumn.net/winter/chat.php?POINTS", totalWinners, totalLosers);
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
                sender.SendMessage("Betting is now CLOSED.");
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
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/x-gzip";
                request.KeepAlive = false;

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
            catch (Exception)
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
}
