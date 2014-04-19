using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Winter;
using WinterBotLogging;

namespace WinterExtensions
{
    public class WinterOptions
    {
        string m_channel;
        string m_url, m_key;

        public string Channel { get; private set; }
        public bool WebApiEnabled { get { Uri uri; return Uri.TryCreate(m_url, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttp; } }
        public string WebApi { get { return m_url; } }
        public string WebApiKey { get { return m_key; } }

        public WinterOptions(Options options)
        {
            Channel = options.Channel.ToLower();

            IniSection section = options.IniReader.GetSectionByName("winter");
            if (section == null)
                return;

            section.GetValue("WebApi", ref m_url);
            section.GetValue("ApiKey", ref m_key);
        }
    }

}
