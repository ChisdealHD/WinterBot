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
        StringQueue m_queue;
        long m_viewerSeconds;
        DateTime m_lastUpdate = DateTime.Now;

        public ViewerCountLogger(WinterBot bot)
        {
            m_queue = new StringQueue(bot, "viewers");

            bot.ViewerCountChanged += bot_ViewerCountChanged;
            bot.StreamOnline += bot_StreamOnline;
            bot.StreamOffline += bot_StreamOffline;
        }

        void WriteLine(string fmt, params object[] objs)
        {
            m_queue.Add(string.Format("[{0}] {1}", DateTime.Now, string.Format(fmt, objs)));
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
    }
}
