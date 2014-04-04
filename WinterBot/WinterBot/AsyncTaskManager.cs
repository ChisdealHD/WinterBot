using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Winter
{
    public interface IIntervalCallback
    {
        TimeSpan Interval { get; }
        Action Callback { get; }
    }

    class TaskKey : IComparable<TaskKey>
    {
        IIntervalCallback m_task;

        public TimeSpan Interval { get; private set; }

        public DateTime LastCalled { get; private set; }

        public bool Elapsed { get { return NextTime <= DateTime.Now; } }

        public DateTime NextTime { get { return LastCalled.Add(Interval); } }

        public void Call()
        {
            LastCalled = DateTime.Now;
            Interval = m_task.Interval;

            m_task.Callback();
        }

        public TaskKey(IIntervalCallback task)
        {
            m_task = task;
            Interval = task.Interval;
            LastCalled = DateTime.Now;
        }

        public int CompareTo(TaskKey rhs)
        {
            if (rhs == null)
                throw new ArgumentNullException("rhs");

            if (this == rhs)
                return 0;

            int result = NextTime.CompareTo(rhs.NextTime);
            return result != 0 ? result : -1;
        }
    }

    public class AsyncTaskManager
    {
        Thread m_thread;
        volatile bool m_shutdown;
        AutoResetEvent m_event = new AutoResetEvent(false);
        object m_sync = new object();
        SortedDictionary<TaskKey, TaskKey> m_tasks = new SortedDictionary<TaskKey,TaskKey>();

        public AsyncTaskManager(WinterBot bot)
        {
            m_thread = new Thread(ThreadProc);
            m_thread.Start();

            if (bot != null)
            {
                bot.BeginShutdown += m_bot_BeginShutdown;
                bot.EndShutdown += m_bot_EndShutdown;
            }
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

        public void Add(IIntervalCallback task)
        {
            if (task == null)
                throw new ArgumentNullException("task");

            AddInternal(new TaskKey(task));
            m_event.Set();
        }

        private void AddInternal(TaskKey task)
        {
            if (task == null)
                return;

            lock (m_sync)
                m_tasks.Add(task, task);
        }

        private TaskKey Next(out TimeSpan nextDuration)
        {
            lock (m_sync)
            {
                if (m_tasks.Count == 0)
                {
                    nextDuration = new TimeSpan(0, 0, 1);
                    return null;
                }

                var min = m_tasks.First().Key;
                if (!min.Elapsed)
                {
                    nextDuration = min.NextTime - DateTime.Now;
                    return null;
                }

                m_tasks.Remove(min);

                nextDuration = min.Interval;
                if (m_tasks.Count > 0)
                {
                    var next = m_tasks.First().Key.NextTime - DateTime.Now;
                    if (nextDuration > next)
                        nextDuration = next;
                }

                return min;
            }
        }
        
        void ThreadProc()
        {
            TimeSpan minSleep = new TimeSpan(0, 0, 0, 0, 250);
            TimeSpan sleep = minSleep;
            while (!m_shutdown)
            {
                if (sleep < minSleep)
                    sleep = minSleep;

                m_event.WaitOne(sleep);

                TaskKey next = Next(out sleep);
                if (next == null)
                    continue;

                next.Call();
                AddInternal(next);
                sleep.Add(minSleep);
            }

            lock (m_sync)
                foreach (var task in m_tasks)
                    task.Key.Call();
        }
    }
}
