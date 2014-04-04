using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Winter
{
    public abstract class SavableDictionary<K,V> : AutoSave
    {
        string m_filename;
        Dictionary<K, V> m_data = new Dictionary<K, V>();
        bool m_dirty;
        object m_saveSync = new object();

        public SavableDictionary(WinterBot bot, string name)
            : base(bot)
        {
            m_filename = Path.Combine(bot.Options.DataDirectory, bot.Channel + "_" + name + ".txt");
            LoadAsync();
        }

        public override string Filename
        {
            get { return m_filename; }
        }


        public V this[K key]
        {
            get
            {
                lock (m_data)
                    return m_data[key];
            }

            set
            {
                lock (m_data)
                {
                    m_data[key] = value;
                    m_dirty = true;
                }
            }
        }

        public void Remove(K key)
        {
            lock (m_data)
            {
                m_data.Remove(key);
                m_dirty = true;
            }
        }

        public bool ContainsKey(K key)
        {
            lock (m_data)
                return m_data.ContainsKey(key);
        }

        public bool TryGetValue(K key, out V value)
        {
            lock (m_data)
                return m_data.TryGetValue(key, out value);
        }

        public V[] Values
        {
            get
            {
                lock (m_data)
                    return m_data.Values.ToArray();
            }
        }


        public override void Save()
        {
            if (!m_dirty)
                return;

            lock (m_saveSync)
            {
                List<Tuple<K, V>> data = null;
                lock (m_data)
                {
                    data = new List<Tuple<K, V>>(m_data.Select(p => new Tuple<K, V>(p.Key, p.Value)));
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
                    foreach (Tuple<K, V> t in Deserialize(File.ReadLines(m_filename)))
                        m_data[t.Item1] = t.Item2;
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

        protected abstract IEnumerable<Tuple<K, V>> Deserialize(IEnumerable<string> lines);
        protected abstract IEnumerable<string> Serialize(IEnumerable<Tuple<K, V>> values);
    }
}
