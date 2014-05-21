using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter
{
    class AutoPoll
    {
        DateTime m_lastStop = DateTime.Now;
        bool m_active = false;
        DateTime m_lastVote = DateTime.Now;
        DateTime m_lastMessage = DateTime.Now;
        Dictionary<TwitchUser, int> m_result = new Dictionary<TwitchUser, int>();

        bool m_dirty = false;
        AutoPollOptions m_options;

        public AutoPoll(WinterBot bot)
        {
            m_options = bot.Options.AutoPollOptions;
            if (!m_options.Enabled)
                return;

            bot.MessageReceived += bot_MessageReceived;
            bot.Tick += bot_Tick;
        }


        [BotCommand(AccessLevel.Mod, "voteclear", "clearvote", "closevote", "voteclose")]
        public void ClearVote(WinterBot bot, TwitchUser user, string cmd, string value)
        {
            m_lastStop = DateTime.Now;
            Reset(bot);
        }


        [BotCommand(AccessLevel.Mod, "newvote", "vote")]
        public void NewVote(WinterBot bot, TwitchUser user, string cmd, string value)
        {
            Reset(bot);
        }

        void bot_MessageReceived(WinterBot sender, TwitchUser user, string text)
        {
            if (m_lastStop.Elapsed().TotalSeconds < m_options.VoteClearTimer)
                return;

            if (m_active && m_lastVote.Elapsed().TotalSeconds >= m_options.VoteTimeout)
                Reset(sender);

            int result = -1;
            for (int i = 1; i <= m_options.MaxVoteValue; ++i)
            {
                if (text.Contains(i.ToString()))
                {
                    if (result != -1)
                    {
                        result = -1;
                        break;
                    }

                    result = i;
                }
            }

            if (result != -1)
            {
                m_result[user] = result;
                m_lastVote = DateTime.Now;
                m_dirty = true;
                if (!m_active)
                {
                    m_lastMessage = DateTime.Now;
                    m_active = true;
                }
            }
        }

        void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            if (!m_active)
                return;

            if (m_lastMessage.Elapsed().TotalSeconds > m_options.ReportTime && m_result.Count >= m_options.VoteThreshold)
                ReportTotal(sender);

            if (m_lastVote.Elapsed().TotalSeconds >= m_options.VoteTimeout)
                Reset(sender);
        }

        private void ReportTotal(WinterBot sender)
        {
            if (!m_dirty)
                return;

            var votes = from item in m_result
                        group item by item.Value into g
                        let key = g.Key
                        let count = g.Sum(p => p.Key.IsSubscriber ? m_options.SubVoteCount : 1)
                        orderby key
                        select new
                        {
                            Option = key,
                            Votes = count
                        };

            var top = (from vote in votes orderby vote.Votes descending select vote).First();

            string msg = "@winter Current vote is for {0} with {1} votes. {2}";
            if (!m_active)
                msg = "@winter Voting closed.  Result: {0} with {1} votes. {2}";

            sender.SendResponse(Importance.Med, msg, top.Option, top.Votes, "(" + string.Join(", ", votes.Select(v => string.Format("Option {0}: {1} votes", v.Option, v.Votes))) + ".)");
            m_lastMessage = DateTime.Now;
            m_dirty = false;
        }

        private void Reset(WinterBot sender)
        {
            if (m_active && m_dirty)
            {
                m_active = false;
                ReportTotal(sender);
            }

            m_result.Clear();

            m_lastVote = DateTime.Now;
            m_lastMessage = DateTime.Now;
            m_active = false;
            m_dirty = false;
        }
    }
}
