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
        WinterBot m_bot;
        DateTime m_lastMessage = DateTime.Now;
        Random m_random = new Random();
        int m_curr;
        int m_totalMessages;
        AutoMessageOptions m_msgOptions;
        ChatOptions m_chatOptions;
        bool m_running;
        bool m_checkingMessages;

        bool ShouldEnable
        {
            get
            {
                return !m_running && m_msgOptions.Enabled && m_bot.IsStreamLive;
            }
        }

        public AutoMessage(WinterBot bot)
        {
            m_bot = bot;
            var options = bot.Options;
            m_msgOptions = options.AutoMessageOptions;
            m_chatOptions = options.ChatOptions;

            if (ShouldEnable)
                Enable();

            if (!string.IsNullOrEmpty(m_chatOptions.SubscribeMessage))
                bot.UserSubscribed += bot_UserSubscribed;

            if (!string.IsNullOrEmpty(m_chatOptions.FollowMessage))
                bot.UserFollowed += bot_UserFollowed;

            bot.StreamOnline += bot_StreamOnline;
            bot.StreamOffline += bot_StreamOffline;
        }
        
        [BotCommand(AccessLevel.Mod, "automessage")]
        public void AutoMessageMode(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            bool shouldEnable = false;
            if (value.Trim().ParseBool(ref shouldEnable))
            {
                m_msgOptions.Enabled = shouldEnable;
                if (shouldEnable)
                {
                    if (ShouldEnable)
                        Enable();
                    else
                        Disable();
                }

                sender.SendResponse(Importance.Med, "Auto message now {0}.", shouldEnable ? "enabled" : "disabled");
            }
            else
            {
                sender.SendResponse(Importance.Med, "Auto message is currently {0}.", m_msgOptions.Enabled ? "enabled" : "disabled");
            }
        }

        void bot_StreamOnline(WinterBot sender)
        {
            if (ShouldEnable)
                Enable();
        }

        void bot_StreamOffline(WinterBot sender)
        {
            if (m_running)
                Disable();
        }

        private void Enable()
        {
            if (m_running)
                return;

            m_running = true;
            m_checkingMessages = m_msgOptions.MessageDelay > 0;
            if (m_checkingMessages)
                m_bot.MessageReceived += bot_MessageReceived;

            m_bot.Tick += bot_Tick;

            m_totalMessages = 0;
            m_lastMessage = DateTime.Now;
        }

        private void Disable()
        {
            if (!m_running)
                return;

            m_running = false;
            if (m_checkingMessages)
                m_bot.MessageReceived -= bot_MessageReceived;

            m_bot.Tick -= bot_Tick;
        }
        
        void bot_UserSubscribed(WinterBot sender, TwitchUser user)
        {
            var subMessage = m_chatOptions.SubscribeMessage;
            if (!string.IsNullOrWhiteSpace(subMessage))
                sender.SendMessage(Importance.High, "{0}: {1}", user.Name, subMessage);
        }

        private void bot_UserFollowed(WinterBot sender, TwitchUser user)
        {
            var msg = m_chatOptions.FollowMessage;
            if (!string.IsNullOrWhiteSpace(msg))
                sender.SendMessage(Importance.Low, "{0}: {1}", user.Name, msg);
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

                sender.SendMessage(Importance.Low, msg);
            }
        }
    }
}
