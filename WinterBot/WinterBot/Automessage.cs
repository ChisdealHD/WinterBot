using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Winter
{
    class AutoMessage
    {
        DateTime m_lastMessage = DateTime.Now;
        Random m_random;
        int m_curr;
        int m_delay = 10;
        int m_totalMessages;
        int m_messageDelay;
        List<string> m_messages;
        string m_subMessage, m_followMessage;

        public AutoMessage(WinterBot bot)
        {
            var options = bot.Options;
            var autoMessage = options.GetSectionByName("automessage");
            var messages = options.GetSectionByName("messages");

            if (options.AutoMessage && messages != null)
            {
                m_messages = new List<string>(messages.EnumerateRawStrings());
            }

            if (autoMessage != null)
            {
                autoMessage.GetValue("delay", ref m_delay);
                if (m_delay <= 0)
                    m_delay = 10;

                bool random = true;
                autoMessage.GetValue("random", ref random);
                if (random)
                    m_random = new Random();

                autoMessage.GetValue("messageDelay", ref m_messageDelay);
            }
            else
            {
                m_delay = 10;
                m_random = null;
                m_messageDelay = 25;
            }

            if (m_messageDelay > 0)
                bot.MessageReceived += bot_MessageReceived;

            if (m_messages.Count > 0)
                bot.Tick += bot_Tick;

            var chatOptions = options.ChatOptions;
            m_subMessage = chatOptions.SubscribeMessage;
            m_followMessage = chatOptions.FollowMessage;

            if (!string.IsNullOrEmpty(m_subMessage))
                bot.UserSubscribed += bot_UserSubscribed;

            if (!string.IsNullOrEmpty(m_followMessage))
                bot.UserFollowed += bot_UserFollowed;
        }

        void bot_UserSubscribed(WinterBot sender, TwitchUser user)
        {
            sender.SendMessage("{0}: {1}", user.Name, m_subMessage);
        }

        private void bot_UserFollowed(WinterBot sender, TwitchUser user)
        {
            sender.SendMessage("{0}: {1}", user.Name, m_followMessage);
        }

        void bot_MessageReceived(WinterBot sender, TwitchUser user, string text)
        {
            m_totalMessages++;
        }

        void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            if (m_lastMessage.Elapsed().TotalMinutes >= m_delay &&
                m_totalMessages >= m_messageDelay &&
                sender.LastMessageSent.Elapsed().TotalSeconds >= 45)
            {
                m_lastMessage = DateTime.Now;
                m_totalMessages = 0;

                string msg = null;
                if (m_random != null)
                    msg = m_messages[m_random.Next(m_messages.Count)];
                else
                    msg = m_messages[m_curr];

                m_curr = ++m_curr % m_messages.Count;
                sender.SendMessage(msg);
            }
        }
    }
}
