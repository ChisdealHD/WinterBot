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
        int[] m_result = new int[4];

        int m_lastTotal = 0;
        int m_total = 0;

        public AutoPoll(WinterBot bot)
        {
            bot.MessageReceived += bot_MessageReceived;
            bot.Tick += bot_Tick;
        }


        [BotCommand(AccessLevel.Mod, "voteclear", "clearvote", "closevote")]
        public void ClearVote(WinterBot bot, TwitchUser user, string cmd, string value)
        {
            m_lastStop = DateTime.Now;
            Reset();
        }


        [BotCommand(AccessLevel.Mod, "newvote", "vote")]
        public void NewVote(WinterBot bot, TwitchUser user, string cmd, string value)
        {
            Reset();
        }

        void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            if (m_lastVote.Elapsed().TotalSeconds >= 60)
                Reset();

            if (m_lastMessage.Elapsed().TotalSeconds > 10 && m_active && m_total > 15 && (m_lastTotal != m_total))
            {
                int curr = 0;
                int max = 0;
                for (int i = 0; i < m_result.Length; ++i)
                {
                    if (m_result[i] > curr)
                    {
                        curr = m_result[i];
                        max = i+1;
                    }
                }
                
                sender.SendResponse(Importance.Med, "@winter Current vote is for {0} with {1} votes.", max, curr);
                m_lastMessage = DateTime.Now;
                m_lastTotal = m_total;

                if (m_lastVote.Elapsed().TotalSeconds >= 15)
                    m_active = false;
            }
        }

        void bot_MessageReceived(WinterBot sender, TwitchUser user, string text)
        {
            if (m_lastStop.Elapsed().TotalSeconds < 120)
                return;

            int result = 0;

            if (text.Contains("1"))
                result += 1;

            if (text.Contains("2"))
                if (result == 0)
                    result = 2;
                else
                    result = 27;


            if (text.Contains("3"))
                if (result == 0)
                    result = 3;
                else
                    result = 27;

            if (text.Contains("4"))
                if (result == 0)
                    result = 4;
                else
                    result = 27;

            if (result == 27)
                result = 0;

            if (result != 0)
            {

                m_lastVote = DateTime.Now;
                m_result[result - 1] += user.IsSubscriber ? 3 : 1;
                m_active = true;
                m_total++;
            }


            if (m_active && m_lastVote.Elapsed().TotalSeconds >= 30)
                Reset();
        }

        private void Reset()
        {
            for (int i = 0; i < m_result.Length; ++i)
                m_result[i] = 0;

            m_lastVote = DateTime.Now;
            m_lastMessage = DateTime.Now;
            m_active = false;
            m_total = 0;
            m_lastTotal = 0;
        }
    }
}
