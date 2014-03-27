using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter
{
    class ConcurrentHashset<T> : IEnumerable<T>
    {
        HashSet<T> m_data = new HashSet<T>();

        public ConcurrentHashset()
        {
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
                m_data.Add(t);
        }

        public void Remove(T t)
        {
            lock (m_data)
                m_data.Remove(t);
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
                foreach (var item in items)
                    m_data.Add(item);
        }
    }
}
