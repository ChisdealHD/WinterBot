using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Winter;

namespace WinterExtensions
{
    public class Betting
    {
        string m_stream;
        string m_dataDirectory;

        Dictionary<string, int> m_points = new Dictionary<string, int>();
        HashSet<TwitchUser> m_pointsRequest = new HashSet<TwitchUser>();
        Dictionary<TwitchUser, Tuple<string, int>> m_confirmRequest = new Dictionary<TwitchUser, Tuple<string, int>>();

        DateTime m_lastMessage = DateTime.Now;
        DateTime m_lastOpenUpdate = DateTime.Now;
        BettingRound m_currentRound, m_lastRound;
        ConcurrentQueue<BettingRound> m_toSave = new ConcurrentQueue<BettingRound>();
        
        Thread m_saveThread;
        bool m_shutdown;
        object m_sync = new object();

        public Betting(WinterBot bot)
        {
            m_dataDirectory = bot.Options.Data;
            m_stream = bot.Options.Channel;
            LoadPoints();
            bot.Tick += bot_Tick;
            bot.BeginShutdown += bot_BeginShutdown;
            bot.EndShutdown += bot_EndShutdown;
        }

        bool IsBettingOpen { get { return m_currentRound != null && m_currentRound.Open; } }

        bool WaitingResult { get { return m_currentRound != null && m_currentRound.Result == null; } }


        [BotCommand(AccessLevel.Mod, "openbetting", "openbet", "startbet", "startbetting")]
        public void OpenBetting(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (IsBettingOpen || WaitingResult)
            {
                sender.SendMessage("Betting is currently ongoing.  Use !result to award points, use !cancelbet to cancel the current bet.");
                return;
            }

            bool confirm;
            int time;
            HashSet<string> values = ParseOpenBet(sender, value, out confirm, out time);
            if (values == null)
                return;

            if (values.Count < 2)
            {
                sender.SendMessage("Usage: '!openbetting option1 option2'.");
                return;
            }

            m_currentRound = new BettingRound(this, user, values, confirm, time);
            WriteOpenBetMessage(sender);
        }

        [BotCommand(AccessLevel.Mod, "cancelbetting", "cancelbet")]
        public void CancelBetting(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (m_currentRound == null)
            {
                sender.SendMessage("Betting is not currently open.");
                return;
            }

            CancelBetting(sender);
        }

        [BotCommand(AccessLevel.Normal, "bet")]
        public void BetCommand(WinterBot sender, TwitchUser user, string cmd, string value)
        {
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
            m_pointsRequest.Add(user);
        }
        
        [BotCommand(AccessLevel.Mod, "result", "winner")]
        public void ResultCommand(WinterBot sender, TwitchUser user, string cmd, string result)
        {
            var round = m_currentRound != null ? m_currentRound : m_lastRound;
            if (round == null)
            {
                sender.SendMessage("Not currently waiting for results.");
                return;
            }

            result = result.Trim().ToLower();
            if (!round.Values.Contains(result))
            {
                sender.SendMessage("{0}: '{1}' is not a voting option, options are: {3}", user.Name, result, string.Join(", ", round.Values));
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
                    sender.SendMessage("Bet complete, results are tallied.  {0} people won, {1} people lost.", round.Winners, round.Losers);
                else
                    sender.SendMessage("Bet complete (no bets).");
            }
            else
            {
                sender.SendMessage("Rolled back betting result '{0}', new result '{1}'.", oldResult, result);
            }

            lock (m_sync)
            {
                if (m_currentRound != null)
                {
                    if (m_lastRound != null)
                        m_toSave.Enqueue(m_lastRound);

                    m_lastRound = m_currentRound;
                    m_currentRound = null;
                }
            }

            SavePoints();
        }

        void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            if (IsBettingOpen)
            {
                if (m_currentRound.OpenTime.Elapsed().TotalSeconds >= m_currentRound.Time)
                {
                    m_currentRound.Open = false;
                    sender.SendMessage("Betting is now closed.");
                }
                else if (m_currentRound.Time > 60 && m_lastOpenUpdate.Elapsed().TotalSeconds > 60)
                {
                    WriteOpenBetMessage(sender);
                }
            }

            if (m_confirmRequest.Count > 0)
            {
                if (m_confirmRequest.Count == 1)
                {
                    var item = m_confirmRequest.First();
                    sender.SendMessage("{0}: Bet {1} points for {2}.", item.Key.Name, item.Value.Item2, item.Value.Item1);
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
                    sender.SendMessage(sb.ToString());

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
                    string user = m_pointsRequest.First().Name;
                    int points = GetPoints(user);

                    sender.SendMessage("{0}: You have {1} points.", user, points);
                }
                else
                {
                    sender.SendMessage("Point totals: " + string.Join(", ", from user in m_pointsRequest
                                                                            let name = user.Name
                                                                            let points = GetPoints(name)
                                                                            select string.Format("{0} has {1} points", name, points)));
                }

                m_pointsRequest.Clear();
                m_lastMessage = DateTime.Now;
            }

            if (m_lastRound != null && m_lastRound.CloseTime.Elapsed().TotalMinutes >= 5)
            {
                SaveLastRound();
            }
        }

        void bot_BeginShutdown(WinterBot sender)
        {
            m_shutdown = true;
            SaveLastRound();
        }

        void bot_EndShutdown(WinterBot sender)
        {
            if (m_saveThread != null)
                m_saveThread.Join();
        }

        private void SaveLastRound()
        {
            lock (m_sync)
            {
                if (m_lastRound != null)
                {
                    m_toSave.Enqueue(m_lastRound);
                    m_lastRound = null;

                    if (m_saveThread == null)
                    {
                        m_saveThread = new Thread(SaveThreadProc);
                        m_saveThread.Start();
                    }
                }
            }
        }

        private void SaveThreadProc()
        {
            while (!m_shutdown)
            {
                DateTime lastSave = DateTime.Now;
                while (!m_shutdown && lastSave.Elapsed().TotalMinutes < 5)
                    Thread.Sleep(250);

                if (m_toSave.Count == 0)
                    continue;

                string filename = Path.Combine(m_dataDirectory, "logs", m_stream + "_pointlog.txt");
                File.AppendAllLines(filename, m_toSave.Enumerate().Select(o => o.ToString()));
            }
        }

        internal void AddPoints(TwitchUser user, int points)
        {
            string name = user.Name.ToLower();
            m_points[name] = GetPoints(name) + points;
        }

        private int GetPoints(string user)
        {
            int curr;
            if (!m_points.TryGetValue(user, out curr))
                curr = 100;
            return curr;
        }

        private void SendMessage(WinterBot bot, string fmt, params object[] args)
        {
            if (m_lastMessage.Elapsed().TotalSeconds >= 10)
            {
                bot.SendMessage(fmt, args);
                m_lastMessage = DateTime.Now;
            }
        }

        private void LoadPoints()
        {
            string fullPath = Path.Combine(m_dataDirectory, m_stream + "_points.txt");

            m_points.Clear();
            if (File.Exists(fullPath))
            {
                foreach (string line in File.ReadLines(fullPath))
                {
                    string[] values = line.Split(' ');
                    m_points[values[0].ToLower()] = int.Parse(values[1]);
                }
            }
        }

        private void SavePoints()
        {
            string fullPath = Path.Combine(m_dataDirectory, m_stream + "_points.txt");
            File.WriteAllLines(fullPath, from item in m_points select string.Format("{0} {1}", item.Key, item.Value));
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
                            bot.SendMessage("Usage: '!openbetting -time=[seconds] option1 option2'.  Minimum of 30 seconds, maximum of 600 seconds.");
                            return null;
                        }
                    }
                    else
                    {
                        bot.SendMessage("!openbetting: unknown option '{0}'", item);
                        return null;
                    }
                }

                result.Add(item);
            }

            return result;
        }

        private bool ParseBet(WinterBot bot, TwitchUser user, string value, out string who, out int bet)
        {
            who = null;
            bet = 0;

            value = value.ToLower();
            string[] args = value.Split(new char[] { ' ' }, 2);
            if (args.Length != 2)
            {
                SendMessage(bot, "{0}:  Usage:  !bet [who] [amount].  (Minimum bet is 1, maximum bet is 100.)", user.Name);
                return false;
            }

            who = args[0];
            if (!m_currentRound.IsValidOption(who))
            {
                SendMessage(bot, "{0}: {1} is not a valid option.  Options are: {2}.", user.Name, who, string.Join(", ", m_currentRound.Values));
                return false;
            }

            string betString = args[1].ToLower();
            if (!int.TryParse(args[1], out bet))
            {
                if (betString == "min")
                {
                    bet = 1;
                }
                else if (betString == "max")
                {
                    bet = 100;
                }
                else
                {
                    SendMessage(bot, "{0}:  Usage:  !bet [who] [amount].  (Minimum bet is 1, maximum bet is 100.)", user.Name);
                    return false;
                }
            }

            if (bet <= 0)
                bet = 1;
            else if (bet > 100)
                bet = 100;

            return true;
        }

        private void CancelBetting(WinterBot sender)
        {
            if (m_currentRound != null)
            {
                sender.SendMessage("Betting is cancelled.");
                m_currentRound.Open = false;
                m_currentRound = null;
            }
        }
        

        private void WriteOpenBetMessage(WinterBot sender)
        {
            sender.SendMessage("Betting is now open, use '!bet [player] [amount]' to bet.  Current players: {0}.  You may bet up to 100 points, betting closes in {1} seconds.", string.Join(", ", m_currentRound.Values), m_currentRound.Time);
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

            sb.AppendFormat("Bet on: {0} ran from {1} - {2}, with result {3}\n", string.Join(", ", Values), OpenTime, CloseTime, Result);
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
