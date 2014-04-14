using System;
using System.Collections.Generic;
using System.Linq;

namespace Winter
{

    public abstract class AutoSave : IIntervalCallback
    {
        static TimeSpan s_saveInterval = new TimeSpan(0, 5, 0);

        public AutoSave(WinterBot bot)
        {
            Bot = bot;
            ConcurrentSaver.Instance.Add(this);
        }


        public WinterBot Bot { get; private set; }

        abstract public void Save();

        public TimeSpan Interval
        {
            get { return s_saveInterval; }
        }

        public Action Callback
        {
            get { return Save; }
        }
    }


    class ConcurrentSaver
    {
        static ConcurrentSaver s_instance = new ConcurrentSaver();
        Dictionary<WinterBot, AsyncTaskManager> m_threads = new Dictionary<WinterBot, AsyncTaskManager>();

        public static ConcurrentSaver Instance { get { return s_instance; } }

        private ConcurrentSaver()
        {
        }

        public void Add(AutoSave savable)
        {
            lock (m_threads)
            {
                var bot = savable.Bot;
                AsyncTaskManager value;
                if (!m_threads.TryGetValue(savable.Bot, out value))
                {
                    m_threads[bot] = value = new AsyncTaskManager(bot);
                    bot.EndShutdown += bot_EndShutdown;
                }

                value.Add(savable);
            }
        }

        void bot_EndShutdown(WinterBot sender)
        {
            lock (m_threads)
                m_threads.Remove(sender);
        }
    }
}
