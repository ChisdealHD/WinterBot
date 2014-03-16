using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Winter;

namespace WinterExtensions
{
    class Bet
    {
        public string User { get; set; }
        public int Amount { get; set; }

        public Bet(string user, int amount)
        {
            User = user;
            Amount = amount;
        }
    }

    class BetTracker
    {
        public string Value { get; set; }

        public Dictionary<string, Bet> m_bets = new Dictionary<string, Bet>();

        public IEnumerable<Bet> Bets { get { return m_bets.Values; } }

        public BetTracker(string value)
        {
            Value = value;
        }

        public void AddBet(TwitchUser u, int bet)
        {
            string user = u.Name;
            m_bets[user] = new Bet(user, bet);
        }

        internal void RemoveBet(TwitchUser user)
        {
            if (m_bets.ContainsKey(user.Name))
                m_bets.Remove(user.Name);
        }
    }

    public class Betting
    {
        public Betting(WinterBot bot)
        {
            m_dataDirectory = bot.Options.Data;
            m_stream = bot.Options.Channel;
            LoadPoints();
            bot.Tick += bot_Tick;
        }

        Dictionary<string, BetTracker> m_bets = new Dictionary<string, BetTracker>();
        bool m_open, m_waitingResult;
        Stopwatch m_timer = new Stopwatch();
        Dictionary<string, int> m_points = new Dictionary<string, int>();
        HashSet<TwitchUser> m_pointsRequest = new HashSet<TwitchUser>();
        string m_stream;
        bool m_confirm;
        DateTime m_lastMessage = DateTime.Now;
        string m_dataDirectory;

        [BotCommand(AccessLevel.Mod, "openbetting", "openbet", "startbet", "startbetting")]
        public void OpenBetting(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (m_open || m_waitingResult)
            {
                sender.SendMessage("Betting is currently ongoing.  Use !result to award points, use !cancelbet to cancel the current bet.");
                return;
            }

            string[] values = value.Split(' ', ',');
            
            m_confirm = values.Contains("-confirm");
            if (m_confirm)
                values = (from s in values where !s.StartsWith("-") select s.ToLower()).ToArray();

            if (values.Length < 2)
            {
                sender.SendMessage("Usage: '!openbetting option1 option2'.");
                return;
            }

            m_bets.Clear();
            foreach (var val in from v in values where !v.StartsWith("-") select v)
                m_bets[val] = new BetTracker(val);

            string confirm = "";
            if (m_confirm)
                confirm = " (Bets will be confirmed by the bot.)";

            sender.SendMessage("Betting is now open, use '!bet [player] [amount]' to bet.  Current players: {0}.  You may bet up to 100 points, betting closes in 60 seconds.{1}", string.Join(", ", m_bets.Keys), confirm);
            m_open = true;
            m_waitingResult = true;
            m_timer.Restart();
        }

        [BotCommand(AccessLevel.Mod, "cancelbetting", "cancelbet")]
        public void CancelBetting(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!m_open && !m_waitingResult)
            {
                sender.SendMessage("Betting is not currently open.");
                return;
            }

            m_open = false;
            m_waitingResult = false;

            sender.SendMessage("Betting is cancelled.");
            m_bets.Clear();
        }
        
        [BotCommand(AccessLevel.Normal, "bet")]
        public void BetCommand(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!m_open)
            {
                SendMessage(sender, "{0}: Betting is not currently open.", user.Name);
                return;
            }

            string[] args = value.Split(new char[] { ' ' }, 2);
            string who = args[0].ToLower();

            if (args.Length != 2)
            {
                SendMessage(sender, "{0}:  Usage:  !bet [who] [amount].  (Minimum bet is 1, maximum bet is 100.)", user.Name);
                return;
            }

            if (!m_bets.ContainsKey(who))
            {
                SendMessage(sender, "{0}: {1} is not a valid option.  Options are: {2}.", user.Name, who, string.Join(", ", m_bets.Keys));
                return;
            }

            int bet;
            if (!int.TryParse(args[1], out bet))
            {
                SendMessage(sender, "{0}:  Usage:  !bet [who] [amount].  (Minimum bet is 1, maximum bet is 100.)", user.Name);
                return;
            }

            if (bet <= 0)
                bet = 1;
            else if (bet > 100)
                bet = 100;

            if (m_confirm)
                SendMessage(sender, "{0}: Bet for {1}, {2} points confirmed.", user.Name, who, bet);

            ClearBet(user);
            m_bets[who].AddBet(user, bet);
        }

        private void ClearBet(TwitchUser user)
        {
            foreach (var bet in m_bets)
                bet.Value.RemoveBet(user);
        }


        [BotCommand(AccessLevel.Normal, "points")]
        public void PointsCommand(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            m_pointsRequest.Add(user);
        }
        
        [BotCommand(AccessLevel.Mod, "result", "winner")]
        public void ResultCommand(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!m_waitingResult)
            {
                sender.SendMessage("Not currently waiting for results.");
                return;
            }

            value = value.Trim().ToLower();
            if (!m_bets.ContainsKey(value))
            {
                sender.SendMessage("{0}: '{1}' is not a voting option, options are: {3}", user.Name, value, string.Join(", ", m_bets.Keys));
                return;
            }

            m_open = false;
            m_waitingResult = false;

            int winners = 0, losers = 0;

            foreach (var item in m_bets)
            {
                if (item.Key == value)
                {
                    // Winners
                    foreach (var bet in item.Value.Bets)
                    {
                        AddPoints(bet.User, bet.Amount);
                        winners++;
                    }
                }
                else
                {
                    // Losers
                    foreach (var bet in item.Value.Bets)
                    {
                        AddPoints(bet.User, -bet.Amount);
                        losers++;
                    }
                }
            }

            if (winners > 0 || losers > 0)
                sender.SendMessage("Bet complete, results are tallied.  {0} people won, {1} people lost.", winners, losers);
            else
                sender.SendMessage("Bet complete (no bets).");

            m_bets.Clear();
            SavePoints();
        }

        private void AddPoints(string user, int points)
        {
            m_points[user] = GetPoints(user) + points;
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
            var now = DateTime.Now;
            if ((now - m_lastMessage).TotalSeconds >= 7)
            {
                bot.SendMessage(fmt, args);
                m_lastMessage = now;
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

        void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            if (m_open && m_timer.Elapsed.Minutes >= 1)
            {
                m_open = false;
                sender.SendMessage("Betting is now closed.");
            }

            if (m_pointsRequest.Count > 0)
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
            }
        }
    }
}
