using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Winter
{
    public class BotAsyncTask
    {
        WinterBot m_bot;
        bool m_shutdown;
        Callback m_callback;
        Thread m_thread;
        AutoResetEvent m_event = new AutoResetEvent(false);

        public delegate bool Callback(WinterBot bot);

        public TimeSpan Interval { get; set; }

        public bool Started { get { return m_callback != null; } }

        public BotAsyncTask(WinterBot bot, TimeSpan interval)
        {
            m_bot = bot;
            Interval = interval;
        }

        public void StartAsync(Callback callback)
        {
            if (Started)
                throw new InvalidOperationException("Task already started");

            m_callback = callback;

            m_bot.BeginShutdown += m_bot_BeginShutdown;
            m_bot.EndShutdown += m_bot_EndShutdown;

            m_thread = new Thread(ThreadProc);
            m_thread.Start();
        }

        void m_bot_BeginShutdown(WinterBot sender)
        {
            m_shutdown = true;
            m_event.Set();
        }

        void m_bot_EndShutdown(WinterBot sender)
        {
            m_thread.Join();
        }

        void ThreadProc()
        {
            Thread.CurrentThread.Name = m_callback.Method.Name;

            while (!m_shutdown)
            {
                m_event.WaitOne(Interval);
                if (!m_callback(m_bot))
                    break;
            }

            m_bot.BeginShutdown -= m_bot_BeginShutdown;
            m_bot.EndShutdown -= m_bot_EndShutdown;
        }
    }
}
