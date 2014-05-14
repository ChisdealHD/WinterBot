using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Winter;

namespace WinterExtensions
{
    enum Action
    {
        Emote,
        Chat,
        Timeout,
        Ban,
        Clear,
        Subbed
    }
    class ChatMessage
    {
        private DateTime m_time;
        private TwitchUser m_user;
        private string m_text;
        private Action m_action;

        public ChatMessage(DateTime time, TwitchUser user, Action action, string text = null)
        {
            m_time = time;
            m_user = user;
            m_text = text;
            m_action = action;
        }


        public override string ToString()
        {
            return string.Format("{0}\n{1}{2}{3}\n{4}{5}\n", m_time.ToSql(), m_user.Name, GetSub(), GetMod(), GetAction(), m_text ?? "");
        }

        private string GetSub()
        {
            return m_user.IsSubscriber ? " S" : "";
        }

        string GetMod()
        {
            return m_user.IsModerator ? " M" : "";
        }

        private string GetAction()
        {
            switch (m_action)
            {
                case Action.Ban:
                    return "BOT BAN";

                case Action.Emote:
                    return "EMOTED ";

                case Action.Clear:
                    return "CHAT CLEAR";

                case Action.Subbed:
                    return "SUBSCRIBED";

                default:
                    return "";
            }
        }
    }

    class ChatSaver : AutoSave
    {
        object m_sync = new object();
        volatile List<ChatMessage> m_messages = new List<ChatMessage>();
        WinterBot m_bot;
        WinterOptions m_options;
        private HttpManager m_http;

        public override TimeSpan Interval
        {
            get
            {
                return new TimeSpan(0, 0, 30);
            }
        }

        public ChatSaver(WinterBot bot, WinterOptions options)
            : base(bot)
        {
            m_options = options;
            m_bot = bot;

            m_http = new HttpManager(options);
            bot.MessageReceived += bot_MessageReceived;
            bot.ActionReceived += bot_ActionReceived;
            bot.UserSubscribed += bot_UserSubscribed;
            bot.ChatClear += bot_ChatClear;
            bot.UserBanned += bot_UserBanned;
            bot.UserTimedOut += bot_UserTimedOut;
        }

        void bot_UserTimedOut(WinterBot sender, TwitchUser user, int duration)
        {
            lock (m_sync)
                m_messages.Add(new ChatMessage(DateTime.Now, user, Action.Timeout, string.Format("BOT TIMEOUT FOR {0} SECONDS", duration)));
        }

        void bot_UserBanned(WinterBot sender, TwitchUser user)
        {
            lock (m_sync)
                m_messages.Add(new ChatMessage(DateTime.Now, user, Action.Ban));
        }

        void bot_ChatClear(WinterBot sender, TwitchUser user)
        {
            lock (m_sync)
                m_messages.Add(new ChatMessage(DateTime.Now, user, Action.Clear));
        }

        void bot_UserSubscribed(WinterBot sender, TwitchUser user)
        {
            lock (m_sync)
                m_messages.Add(new ChatMessage(DateTime.Now, user, Action.Subbed));
        }

        void bot_ActionReceived(WinterBot sender, TwitchUser user, string text)
        {
            lock (m_sync)
                m_messages.Add(new ChatMessage(DateTime.Now, user, Action.Emote, text));
        }

        void bot_MessageReceived(WinterBot sender, TwitchUser user, string text)
        {
            lock (m_sync)
                m_messages.Add(new ChatMessage(DateTime.Now, user, Action.Chat, text));
        }

        public override void Save()
        {
            StringBuilder data;
            lock (m_sync)
            {
                if (m_messages.Count == 0)
                    return;

                data = new StringBuilder();
                foreach (var msg in m_messages)
                    data.Append(msg.ToString());

                m_messages.Clear();
            }
            
            DateTime now = DateTime.Now;
            string parameters = string.Format("SAVECHAT={0}_{1:00}_{2:00}_{3:00}.txt", Bot.Channel, now.Year, now.Month, now.Day);
            m_http.PostAsync("api.php", parameters, data).Wait();
        }
    }
}
