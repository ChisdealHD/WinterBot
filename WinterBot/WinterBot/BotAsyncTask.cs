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
        volatile bool m_shutdown;
        Callback m_callback;
        volatile Thread m_thread;
        AutoResetEvent m_event = new AutoResetEvent(false);

        public delegate bool Callback(WinterBot bot);

        public TimeSpan Interval { get; private set; }

        public bool Started { get { return m_thread != null; } }

        public BotAsyncTask(WinterBot bot, Callback callback, TimeSpan interval)
        {
            m_bot = bot;
            Interval = interval;
            m_callback = callback;
        }

        public void Stop()
        {
            m_shutdown = true;
            m_event.Set();
        }

        public void Join()
        {
            var thread = m_thread;
            if (thread != null)
            {
                if (!m_shutdown)
                    Stop();

                thread.Join();
                m_thread = null;
            }
        }

        public void StartAsync(bool saveImmediately=false)
        {
            if (m_thread != null)
                throw new InvalidOperationException("Task already started");

            m_bot.BeginShutdown += m_bot_BeginShutdown;
            m_bot.EndShutdown += m_bot_EndShutdown;

            if (saveImmediately)
                m_thread = new Thread(SaveImmediatelyThreadProc);
            else
                m_thread = new Thread(ThreadProc);
            m_thread.Start();
        }

        void m_bot_BeginShutdown(WinterBot sender)
        {
            Stop();
        }

        void m_bot_EndShutdown(WinterBot sender)
        {
            Join();
        }


        void SaveImmediatelyThreadProc()
        {
            if (m_callback(m_bot))
                MainLoop();
        }

        private void MainLoop()
        {
            Thread.CurrentThread.Name = m_callback.Method.Name;

            while (!m_shutdown)
            {
                if (Interval == TimeSpan.Zero || Interval == TimeSpan.MaxValue)
                    m_event.WaitOne();
                else
                    m_event.WaitOne(Interval);

                if (!m_callback(m_bot))
                    break;
            }

            m_bot.BeginShutdown -= m_bot_BeginShutdown;
            m_bot.EndShutdown -= m_bot_EndShutdown;
            m_thread = null;
        }



        void ThreadProc()
        {
            MainLoop();
        }
    }
}
