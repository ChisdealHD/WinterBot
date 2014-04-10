using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Winter
{

    abstract public class ConcurrentHashset<T> : AutoSave, IEnumerable<T>
    {
        HashSet<T> m_data = new HashSet<T>();
        volatile bool m_dirty;
        string m_filename;
        object m_saveSync = new object();

        public override string Filename
        {
            get { return m_filename; }
        }

        public ConcurrentHashset(WinterBot bot, string name)
            : base(bot)
        {
            m_filename = Path.Combine(bot.Options.DataDirectory, bot.Channel + "_" + name + ".txt");
            LoadAsync();
        }

        internal bool TryRemove(T target)
        {
            lock (m_data)
            {
                if (m_data.Contains(target))
                {
                    m_data.Remove(target);
                    return true;
                }
            }

            return false;
        }

        public void Add(T t)
        {
            lock (m_data)
            {
                m_data.Add(t);
                m_dirty = true;
            }
        }

        public void Remove(T t)
        {
            lock (m_data)
            {
                m_data.Remove(t);
                m_dirty = true;
            }
        }

        public bool Contains(T t)
        {
            lock (m_data)
                return m_data.Contains(t);
        }

        public IEnumerator<T> GetEnumerator()
        {
            List<T> data = null;
            lock (m_data)
                data = new List<T>(m_data);

            return data.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            T[] data = null;
            lock (m_data)
                data = m_data.ToArray();

            return data.GetEnumerator();
        }

        public void AddRange(IEnumerable<T> items)
        {
            lock (m_data)
            {
                foreach (var item in items)
                {
                    m_data.Add(item);
                    m_dirty = true;
                }
            }
        }

        public override void Save()
        {
            if (!m_dirty)
                return;

            lock (m_saveSync)
            {
                List<T> data = null;
                lock (m_data)
                {
                    data = new List<T>(m_data);
                    m_dirty = false;
                }

                File.WriteAllLines(m_filename, Serialize(data));
            }

            Bot.WriteDiagnostic(DiagnosticFacility.IO, "Saved file: {0}", Path.GetFileName(m_filename));
        }

        public void Load()
        {
            if (!File.Exists(m_filename))
                return;

            lock (m_saveSync)
            {
                lock (m_data)
                {
                    m_data.Clear();
                    foreach (T t in Deserialize(File.ReadLines(m_filename)))
                        m_data.Add(t);
                }
            }
        }

        public void LoadAsync()
        {
            ThreadPool.QueueUserWorkItem(LoadAsyncWorker);
        }

        private void LoadAsyncWorker(object state)
        {
            Load();
        }

        protected abstract IEnumerable<T> Deserialize(IEnumerable<string> lines);
        protected abstract IEnumerable<string> Serialize(IEnumerable<T> values);
    }

    public class UserSet : ConcurrentHashset<TwitchUser>
    {
        public UserSet(WinterBot bot, string name)
            : base(bot, name)
        {
        }

        protected override IEnumerable<TwitchUser> Deserialize(IEnumerable<string> lines)
        {
            foreach (var line in lines.Select(l=>l.Trim()))
                if (string.IsNullOrWhiteSpace(line) && TwitchUsers.IsValidUserName(line))
                    yield return Bot.Users.GetUser(line);
        }

        protected override IEnumerable<string> Serialize(IEnumerable<TwitchUser> users)
        {
            foreach (var user in users)
                yield return user.Name;
        }
    }
}
