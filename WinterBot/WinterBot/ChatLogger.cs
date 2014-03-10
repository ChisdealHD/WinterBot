using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinterBot
{
    [Serializable]
    abstract class ChatEvent
    {
        public DateTime Timestamp { get; set; }
        public string User { get; set; }

        public ChatEvent(TwitchUser user)
        {
            Timestamp = DateTime.UtcNow;
            User = user.Name;
        }

        public override string ToString()
        {
            return string.Format("[{0}] {1}", Timestamp, User);
        }
    }

    [Serializable]
    class ChatMessage : ChatEvent
    {
        public string Message { get; set; }

        public ChatMessage(TwitchUser user, string message)
            : base(user)
        {
            Message = message;
        }

        public override string ToString()
        {
            return string.Format("{0}: {1}", base.ToString(), Message);
        }
    }

    [Serializable]
    class ChatSubscribeEvent : ChatEvent
    {
        public ChatSubscribeEvent(TwitchUser user)
            : base(user)
        {
        }


        public override string ToString()
        {
            return base.ToString() + " subscribed!";
        }
    }

    [Serializable]
    class ChatClearEvent : ChatEvent
    {
        public ChatClearEvent(TwitchUser user)
            : base(user)
        {
        }

        public override string ToString()
        {
            return base.ToString() + " timed out.";
        }
    }

    [Serializable]
    class ChatBanEvent : ChatEvent
    {
        public ChatBanEvent(TwitchUser user)
            : base(user)
        {
        }

        public override string ToString()
        {
            return base.ToString() + " banned by this bot.";
        }
    }
    [Serializable]
    class ChatTimeout : ChatEvent
    {
        public int Duration { get; set; }
        public ChatTimeout(TwitchUser user, int duration)
            : base(user)
        {
            Duration = duration;
        }

        public override string ToString()
        {
            return string.Format("{0} timed out for {1} seconds by this bot.", base.ToString(), Duration);
        }
    }

    public class ChatLogger
    {
        object m_saveSync = new object();
        object m_sync = new object();
        volatile List<ChatEvent> m_queue = new List<ChatEvent>();
        HashSet<TwitchUser> m_timeouts = new HashSet<TwitchUser>();
        bool m_saveReadableLog = true;
        bool m_saveCompressedLog = true;
        TimeSpan m_lastSave = new TimeSpan();
        string m_stream;

        public ChatLogger(WinterBot bot)
        {
            var options = bot.Options.RawIniData;
            m_stream = bot.Options.Channel;
            var section = options.GetSectionByName("logging");
            if (section != null)
            {
                section.GetValue("saveLog", ref m_saveReadableLog);
                section.GetValue("saveCompressedLog", ref m_saveCompressedLog);
            }

            if (m_saveReadableLog || m_saveCompressedLog)
            {
                bot.MessageReceived += bot_MessageReceived;
                bot.ChatClear += bot_ChatClear;
                bot.UserSubscribed += bot_UserSubscribed;
                bot.UserBanned += bot_UserBanned;
                bot.UserTimedOut += bot_UserTimedOut;
                bot.Tick += bot_Tick;
            }
        }

        void bot_UserTimedOut(WinterBot sender, TwitchUser user, int duration)
        {
            lock (m_sync)
            {
                m_queue.Add(new ChatTimeout(user, duration));
                m_timeouts.Add(user);
            }
        }

        void bot_UserBanned(WinterBot sender, TwitchUser user)
        {
            lock (m_sync)
            {
                m_queue.Add(new ChatBanEvent(user));
                m_timeouts.Add(user);
            }
        }

        void bot_UserSubscribed(WinterBot sender, TwitchUser user)
        {
            Enqueue(new ChatSubscribeEvent(user));
        }

        void bot_ChatClear(WinterBot sender, TwitchUser user)
        {
            lock (m_sync)
            {
                if (m_timeouts.Contains(user))
                    m_timeouts.Remove(user);
                else
                    m_queue.Add(new ChatClearEvent(user));
            }
        }

        void bot_MessageReceived(WinterBot sender, TwitchUser user, string text)
        {
            Enqueue(new ChatMessage(user, text));
        }

        private void Enqueue(ChatEvent evt)
        {
            lock (m_sync)
                m_queue.Add(evt);
        }

        void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            m_lastSave += timeSinceLastUpdate;

            if (m_lastSave.TotalMinutes >= 5)
            {
                ThreadPool.QueueUserWorkItem(SaveToDisk);
                m_lastSave = new TimeSpan();
            }
        }

        private void SaveToDisk(object state)
        {
            lock (m_saveSync)
            {
                List<ChatEvent> events;
                lock (m_sync)
                {
                    events = m_queue;
                    m_queue = new List<ChatEvent>();
                }

                if (events.Count == 0)
                    return;

                var now = DateTime.Now;
                string filename = string.Format("{0}_{1:00}_{2:00}_{3:00}.txt", m_stream, now.Year, now.Month, now.Day);

                if (m_saveReadableLog)
                    File.AppendAllLines(filename, events.Select(evt=>evt.ToString()));

                filename = filename = string.Format("{0}_{1:00}_{2:00}_{3:00}_{4:00}_{5:00}_{6:00}.dat", m_stream, now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second);
                if (m_saveCompressedLog)
                {
                    using (FileStream stream = File.Create(filename))
                    {
                        using (GZipStream gzStream = new GZipStream(stream, CompressionLevel.Optimal))
                        {
                            BinaryFormatter fmt = new BinaryFormatter();
                            fmt.Serialize(gzStream, events);
                        }
                    }
                }
            }
        }
    }
}
