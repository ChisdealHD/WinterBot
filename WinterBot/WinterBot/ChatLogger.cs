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

namespace Winter
{
    [Serializable]
    abstract class ChatEvent
    {
        public DateTime Timestamp { get; set; }
        public string User { get; set; }

        public ChatEvent(TwitchUser user)
        {
            Timestamp = DateTime.Now;
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
    class ChatModEvent : ChatEvent
    {
        private bool m_added;

        [NonSerialized]
        private List<TwitchUser> m_mods;

        public ChatModEvent(TwitchUser user, bool added, IEnumerable<TwitchUser> mods)
            : base(user)
        {
            m_added = added;
            m_mods = new List<TwitchUser>(mods);
        }

        public override string ToString()
        {
            return base.ToString() + string.Format(" {0} chat. ({1})", m_added ? "joined" : "left", string.Join(", ", m_mods));
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
        HashSet<TwitchUser> m_mods = new HashSet<TwitchUser>();
        bool m_saveReadableLog = true;
        bool m_saveCompressedLog = true;
        Thread m_saveThread;
        volatile bool m_shutdown;
        string m_dataDirectory;
        AutoResetEvent m_saveThreadSleep = new AutoResetEvent(false);

        string m_stream;

        public ChatLogger(WinterBot bot)
        {
            var options = bot.Options;
            m_stream = options.Channel;
            m_dataDirectory = options.Data;
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
                bot.ModeratorAdded += bot_ModeratorAdded;
                bot.ModeratorRemoved += bot_ModeratorRemoved;
                bot.BeginShutdown += bot_BeginShutdown;
                bot.EndShutdown += bot_EndShutdown;

                m_saveThread = new Thread(SaveThreadProc);
                m_saveThread.Start();
            }
        }

        void bot_ModeratorRemoved(WinterBot sender, TwitchUser user)
        {
            if (m_mods.Contains(user))
                m_mods.Remove(user);

            lock (m_sync)
                m_queue.Add(new ChatModEvent(user, false, m_mods));
        }

        void bot_ModeratorAdded(WinterBot sender, TwitchUser user)
        {
            m_mods.Add(user);

            lock (m_sync)
                m_queue.Add(new ChatModEvent(user, true, m_mods));
        }

        private void bot_BeginShutdown(WinterBot sender)
        {
            m_shutdown = true;
            m_saveThreadSleep.Set();
        }

        private void bot_EndShutdown(WinterBot sender)
        {
            m_saveThread.Join();
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

        List<ChatEvent> m_binaryEvents = new List<ChatEvent>();

        private void SaveThreadProc()
        {
            while (!m_shutdown)
            {
                DateTime lastSave = DateTime.Now;
                while (!m_shutdown && lastSave.Elapsed().TotalMinutes < 1)
                    if (m_saveThreadSleep.WaitOne(10000))
                        break;

                lock (m_saveSync)
                {
                    List<ChatEvent> events = new List<ChatEvent>();
                    lock (m_sync)
                    {
                        var tmp = m_queue;
                        m_queue = events;
                        events = tmp;
                    }

                    if (events.Count == 0)
                        continue;

                    var now = DateTime.Now;
                    string logPath = Path.Combine(m_dataDirectory, "logs");
                    string filename = string.Format("{0}_{1:00}_{2:00}_{3:00}.txt", m_stream, now.Year, now.Month, now.Day);

                    if (m_saveReadableLog)
                    {
                        Directory.CreateDirectory(logPath);
                        File.AppendAllLines(Path.Combine(logPath, filename), events.Select(evt => evt.ToString()));
                    }

                    if (m_saveCompressedLog)
                    {
                        filename = Path.ChangeExtension(filename, "dat");

                        using (var stream = new FileStream(Path.Combine(logPath, filename), FileMode.Append, FileAccess.Write, FileShare.None))
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
}
