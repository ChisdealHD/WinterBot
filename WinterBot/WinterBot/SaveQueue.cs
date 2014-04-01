using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter
{
    public abstract class SaveQueue<T> : AutoSavable
    {
        volatile List<T> m_data = new List<T>();
        volatile List<T> m_saving = new List<T>();
        object m_sync = new object();
        object m_saveSync = new object();

        public SaveQueue(WinterBot bot)
            : base(bot)
        {
        }

        public void Add(T t)
        {
            lock (m_sync)
                m_data.Add(t);
        }


        public override void Save()
        {
            string fn = Filename;
            lock (m_saveSync)
            {
                lock (m_sync)
                {
                    if (m_data.Count == 0)
                        return;

                    var tmp = m_data;
                    m_data = m_saving;
                    m_saving = tmp;
                }

                File.AppendAllLines(fn, Serialize(m_saving));
                m_saving.Clear();
            }

            Bot.WriteDiagnostic(DiagnosticFacility.IO, "Saved file: {0}", Path.GetFileName(fn));
        }

        protected abstract IEnumerable<string> Serialize(IEnumerable<T> data);
    }

    public class StringQueue : SaveQueue<object>
    {
        string m_filename;

        public StringQueue(WinterBot bot, string name)
            : base(bot)
        {
            m_filename = Path.Combine(bot.Options.DataDirectory, bot.Channel + "_" + name + ".txt");

        }

        protected override IEnumerable<string> Serialize(IEnumerable<object> data)
        {
            return data.Select(o => o.ToString());
        }

        public override string Filename
        {
            get { return m_filename; }
        }
    }
}
