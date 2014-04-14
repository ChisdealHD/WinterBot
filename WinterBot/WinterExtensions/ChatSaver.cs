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
            return string.Format("{0}\n{1}{2}{3}\n{4}{5}\n", m_time.ToString("yyyy-MM-dd HH:mm:ss"), m_user.Name, GetSub(), GetMod(), GetAction(), m_text ?? "");
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
        string m_url;

        public ChatSaver(WinterBot bot)
            : base(bot)
        {
            m_bot = bot;
            if (!bot.Channel.Equals("zlfreebird", StringComparison.CurrentCultureIgnoreCase))
                return;

            var section = m_bot.Options.IniReader.GetSectionByName("chat");
            if (section == null || !section.GetValue("httplogger", ref m_url))
                return;

            Uri uri;
            if (Uri.TryCreate(m_url, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttp)
            {
                bot.MessageReceived += bot_MessageReceived;
                bot.ActionReceived += bot_ActionReceived;
                bot.UserSubscribed += bot_UserSubscribed;
                bot.ChatClear += bot_ChatClear;
                bot.UserBanned += bot_UserBanned;
                bot.UserTimedOut += bot_UserTimedOut;
            }
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
            List<ChatMessage> messages = null;
            lock (m_sync)
            {
                if (m_messages.Count == 0)
                    return;

                messages = m_messages;
                m_messages = new List<ChatMessage>(m_messages.Count);
            }

            if (!Save(messages))
            {
                lock (m_sync)
                {
                    var tmp = m_messages;
                    m_messages = messages;

                    if (m_messages.Count != 0)
                        m_messages.AddRange(tmp);
                }
            }
        }

        private bool Save(List<ChatMessage> messages)
        {
            bool succeeded = false;
            DateTime now = DateTime.Now;
            string file = string.Format("{0}_{1:00}_{2:00}_{3:00}.txt", Bot.Channel, now.Year, now.Month, now.Day);
            string url = string.Format("{0}?{1}={2}", m_url, "APPEND", Path.GetFileName(file));

            try
            {
                WebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/x-gzip";

                Stream requestStream = request.GetRequestStream();
                using (GZipStream gzip = new GZipStream(requestStream, CompressionLevel.Optimal))
                using (StreamWriter stream = new StreamWriter(gzip))
                    foreach (var message in messages)
                        stream.Write(message.ToString());

                string result;
                WebResponse response = request.GetResponse();
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                    result = reader.ReadToEnd();

                succeeded = result == "completed";
            }
            catch (Exception)
            {
                m_bot.WriteDiagnostic(DiagnosticFacility.Error, "Failed to save remote chat list.");
            }

            return succeeded;
        }
    }
}
