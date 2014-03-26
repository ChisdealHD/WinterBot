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
        Random m_random = new Random();
        int m_curr;
        int m_totalMessages;
        AutoMessageOptions m_msgOptions;
        ChatOptions m_chatOptions;

        public AutoMessage(WinterBot bot)
        {
            var options = bot.Options;
            m_msgOptions = options.AutoMessageOptions;
            m_chatOptions = options.ChatOptions;

            // TODO: respond to property changed on these
            if (m_msgOptions.Enabled)
            {
                if (m_msgOptions.MessageDelay > 0)
                    bot.MessageReceived += bot_MessageReceived;

                bot.Tick += bot_Tick;
            }

            if (!string.IsNullOrEmpty(m_chatOptions.SubscribeMessage))
                bot.UserSubscribed += bot_UserSubscribed;

            if (!string.IsNullOrEmpty(m_chatOptions.FollowMessage))
                bot.UserFollowed += bot_UserFollowed;
        }

        void bot_UserSubscribed(WinterBot sender, TwitchUser user)
        {
            var subMessage = m_chatOptions.SubscribeMessage;
            if (!string.IsNullOrWhiteSpace(subMessage))
                sender.SendMessage("{0}: {1}", user.Name, subMessage);
        }

        private void bot_UserFollowed(WinterBot sender, TwitchUser user)
        {
            var msg = m_chatOptions.FollowMessage;
            if (!string.IsNullOrWhiteSpace(msg))
                sender.SendMessage("{0}: {1}", user.Name, msg);
        }

        void bot_MessageReceived(WinterBot sender, TwitchUser user, string text)
        {
            m_totalMessages++;
        }

        void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            if (m_lastMessage.Elapsed().TotalMinutes >= m_msgOptions.Delay &&
                m_totalMessages >= m_msgOptions.MessageDelay &&
                sender.LastMessageSent.Elapsed().TotalSeconds >= 45)
            {
                m_lastMessage = DateTime.Now;
                m_totalMessages = 0;

                var messages = m_msgOptions.Messages;
                if (messages.Length == 0)
                    return;

                m_curr %= messages.Length;

                string msg = null;
                if (m_random != null)
                    msg = messages[m_random.Next(messages.Length)];
                else
                    msg = messages[m_curr++];

                sender.SendMessage(msg);
            }
        }
    }
}
