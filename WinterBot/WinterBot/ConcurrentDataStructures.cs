using System;
using System.Collections.Generic;
using System.Linq;

namespace Winter
{

    public abstract class AutoSavable
    {
        bool m_autoSave;

        public AutoSavable(WinterBot bot)
        {
            Bot = bot;
            AutoSave = true;
        }

        public bool AutoSave
        {
            get
            {
                return m_autoSave;
            }

            set
            {
                if (m_autoSave != value)
                {
                    m_autoSave = value;
                    if (value)
                        ConcurrentSaver.Instance.Add(this);
                    else
                        ConcurrentSaver.Instance.Remove(this);
                }
            }
        }

        public abstract string Filename { get; }

        public WinterBot Bot { get; private set; }

        abstract public void Save();
    }


    class ConcurrentSaver
    {
        class ConcurrentSavePair
        {
            HashSet<AutoSavable> m_items = new HashSet<AutoSavable>();
            BotAsyncTask m_task;

            public ConcurrentSavePair(WinterBot bot)
            {
                m_task = new BotAsyncTask(bot, Callback, new TimeSpan(0, 5, 0));
            }

            private bool Callback(WinterBot bot)
            {
                lock (this)
                {
                    foreach (AutoSavable savable in m_items)
                        savable.Save();
                }

                return true;
            }

            public void Add(AutoSavable savable)
            {
                lock (this)
                {
                    m_items.Add(savable);
                    if (!m_task.Started)
                        m_task.StartAsync(true);
                }
            }

            public void Remove(AutoSavable savable)
            {
                lock (this)
                    if (m_items.Contains(savable))
                        m_items.Remove(savable);
            }

            public void BeginShutdown()
            {
                m_task.Stop();
            }

            public void EndShutdown()
            {
                m_task.Join();
            }
        }


        static ConcurrentSaver s_instance = new ConcurrentSaver();
        Dictionary<WinterBot, ConcurrentSavePair> m_savers = new Dictionary<WinterBot, ConcurrentSavePair>();

        public static ConcurrentSaver Instance { get { return s_instance; } }

        private ConcurrentSaver()
        {
        }

        public void Add(AutoSavable savable)
        {
            lock (m_savers)
            {
                var bot = savable.Bot;
                ConcurrentSavePair value;
                if (!m_savers.TryGetValue(savable.Bot, out value))
                {
                    m_savers[bot] = value = new ConcurrentSavePair(bot);
                    bot.BeginShutdown += bot_BeginShutdown;
                    bot.EndShutdown += bot_EndShutdown;
                }

                value.Add(savable);
            }
        }
        public void Remove(AutoSavable savable)
        {
            lock (m_savers)
            {
                var bot = savable.Bot;
                ConcurrentSavePair value;
                if (!m_savers.TryGetValue(savable.Bot, out value))
                    return;

                value.Remove(savable);
            }
        }

        void bot_EndShutdown(WinterBot sender)
        {
            lock (m_savers)
            {
                ConcurrentSavePair value;
                if (!m_savers.TryGetValue(sender, out value))
                    return;

                value.EndShutdown();
                m_savers.Remove(sender);
            }
        }

        void bot_BeginShutdown(WinterBot sender)
        {
            lock (m_savers)
            {
                ConcurrentSavePair value;
                if (!m_savers.TryGetValue(sender, out value))
                    return;

                value.BeginShutdown();
            }
        }
    }
}
