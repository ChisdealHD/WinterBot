using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter
{
    class AutoMessage
    {
        Stopwatch m_timer = new Stopwatch();
        Random m_random;
        int m_curr;
        int m_delay = 10;
        int m_totalMessages;
        int m_messageDelay;
        List<string> m_messages;

        public AutoMessage(WinterBot bot)
        {
            var options = bot.Options.RawIniData;
            var autoMessage = options.GetSectionByName("automessage");
            var messages = options.GetSectionByName("messages");

            if (autoMessage == null || messages == null)
                return;

            bool enabled = true;
            if (autoMessage.GetValue("enabled", ref enabled) && enabled == false)
                return;

            m_messages = new List<string>(messages.EnumerateRawStrings());
            if (m_messages.Count == 0)
                return;

            autoMessage.GetValue("delay", ref m_delay);
            if (m_delay <= 0)
                return;

            bool random = true;
            autoMessage.GetValue("random", ref random);
            if (random)
                m_random = new Random();

            if (autoMessage.GetValue("messageDelay", ref m_messageDelay) && m_messageDelay > 0)
            {
                bot.MessageReceived += bot_MessageReceived;
            }

            bot.Tick += bot_Tick;
            m_timer.Start();
        }

        void bot_MessageReceived(WinterBot sender, TwitchUser user, string text)
        {
            m_totalMessages++;
        }

        void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            if (m_timer.Elapsed.Minutes >= m_delay && m_totalMessages >= m_messageDelay)
            {
                m_totalMessages = 0;
                m_timer.Restart();

                string msg = null;
                if (m_random != null)
                    msg = m_messages[m_random.Next(m_messages.Count)];
                else
                    msg = m_messages[m_curr++ % m_messages.Count];

                sender.SendMessage(msg);
            }
        }
    }
}
