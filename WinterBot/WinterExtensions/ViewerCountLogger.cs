using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Winter;

namespace WinterExtensions
{
    class ViewerCountLogger : AutoSave
    {
        List<Tuple<DateTime, int>> m_viewers = new List<Tuple<DateTime, int>>();
        object m_sync = new object();
        WinterBot m_bot;
        WinterOptions m_options;
        HttpManager m_http;

        public ViewerCountLogger(WinterBot bot, WinterOptions options)
            : base(bot)
        {
            m_bot = bot;
            m_options = options;

            m_http = new HttpManager(options);
            bot.ViewerCountChanged += bot_ViewerCountChanged;
        }

        void bot_ViewerCountChanged(WinterBot sender, int currentViewerCount)
        {
            lock (m_sync)
                m_viewers.Add(new Tuple<DateTime, int>(DateTime.Now, currentViewerCount));
        }

        public override void Save()
        {
            StringBuilder sb;
            lock (m_sync)
            {
                if (m_viewers.Count == 0)
                    return;

                sb = new StringBuilder();
                foreach (var message in m_viewers)
                    sb.AppendFormat("{0}\n{1}\n", message.Item1.ToSql(), message.Item2);

                m_viewers.Clear();
            }

            m_http.PostAsync("api.php", "VIEWERS", sb).Wait();
        }
    }
}
