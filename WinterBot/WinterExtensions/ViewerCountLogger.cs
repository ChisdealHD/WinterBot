using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Winter;

namespace WinterExtensions
{
    class ViewerCountLogger
    {
        ConcurrentQueue<string> m_queue = new ConcurrentQueue<string>();
        string m_dataDir;
        string m_channel;
        long m_viewerSeconds;
        DateTime m_lastUpdate = DateTime.Now;

        public ViewerCountLogger(WinterBot bot)
        {
            m_dataDir = bot.Options.DataDirectory;
            m_channel = bot.Channel;

            bot.ViewerCountChanged += bot_ViewerCountChanged;
            bot.StreamOnline += bot_StreamOnline;
            bot.StreamOffline += bot_StreamOffline;
            
            BotAsyncTask task = new BotAsyncTask(bot, SaveViewerTotals, new TimeSpan(0, 5, 0));
            task.StartAsync();
        }

        void WriteLine(string fmt, params object[] objs)
        {
            m_queue.Enqueue(string.Format("[{0}] {1}", DateTime.Now, string.Format(fmt, objs)));
        }

        void bot_StreamOffline(WinterBot sender)
        {
            WriteLine("Stream offline, {0} viewer hours.", m_viewerSeconds / (60 * 60));
        }

        void bot_StreamOnline(WinterBot sender)
        {
            WriteLine("Stream online.");

            m_lastUpdate = DateTime.Now;
            m_viewerSeconds = 0;
        }

        void bot_ViewerCountChanged(WinterBot sender, int currentViewerCount)
        {
            m_viewerSeconds += (long)m_lastUpdate.Elapsed().TotalSeconds * currentViewerCount;
            m_lastUpdate = DateTime.Now;

            WriteLine("{0} viewers.", currentViewerCount);
        }

        private bool SaveViewerTotals(WinterBot bot)
        {
            if (m_queue.Count == 0)
                return true;

            string filename = Path.Combine(m_dataDir, m_channel + "_viewers.txt");
            File.AppendAllLines(filename, m_queue.Enumerate());

            return true;
        }
    }
}
