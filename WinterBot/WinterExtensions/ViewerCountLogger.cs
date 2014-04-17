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
        string m_channel;
        string m_url;
        WinterBot m_bot;

        public ViewerCountLogger(WinterBot bot)
            : base(bot)
        {
            m_bot = bot;
            m_channel = bot.Channel.ToLower();
            if (!bot.Channel.Equals("zlfreebird", StringComparison.CurrentCultureIgnoreCase))
                return;

            var section = m_bot.Options.IniReader.GetSectionByName("chat");
            if (section == null || !section.GetValue("httplogger", ref m_url))
                return;

            bot.ViewerCountChanged += bot_ViewerCountChanged;
        }

        void bot_ViewerCountChanged(WinterBot sender, int currentViewerCount)
        {
            lock (m_sync)
                m_viewers.Add(new Tuple<DateTime, int>(DateTime.Now, currentViewerCount));
        }

        public override void Save()
        {
            List<Tuple<DateTime, int>> viewers;

            lock (m_sync)
            {
                if (m_viewers.Count == 0)
                    return;

                viewers = m_viewers;
                m_viewers = new List<Tuple<DateTime, int>>(viewers.Capacity);
            }

            if (!Save(viewers))
            {
                lock (m_sync)
                {
                    var tmp = m_viewers;
                    m_viewers = viewers;

                    if (m_viewers.Count != 0)
                        m_viewers.AddRange(tmp);
                }
            }
        }


        bool Save(List<Tuple<DateTime, int>> viewers)
        {
            bool succeeded = false;
            DateTime now = DateTime.Now;
            string url = string.Format("{0}/viewers.php?CHANNEL={1}", m_url, m_channel);

            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
                request.Method = "POST";
                request.ContentType = "application/x-gzip";
                request.KeepAlive = false;

                Stream requestStream = request.GetRequestStream();
                using (GZipStream gzip = new GZipStream(requestStream, CompressionLevel.Optimal))
                using (StreamWriter stream = new StreamWriter(gzip))
                    foreach (var message in viewers)
                        stream.Write("{0}\n{1}\n", message.Item1.ToSql(), message.Item2);

                string result;
                WebResponse response = request.GetResponse();
                using (Stream stream = response.GetResponseStream())
                using (StreamReader reader = new StreamReader(stream))
                    result = reader.ReadToEnd();

                succeeded = result == "ok";
            }
            catch (Exception)
            {
                m_bot.WriteDiagnostic(DiagnosticFacility.Error, "Failed to save viewer list.");
            }

            return succeeded;
        }
    }
}
