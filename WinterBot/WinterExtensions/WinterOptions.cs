using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Winter;

namespace WinterExtensions
{
    public class WinterOptions
    {
        string m_channel;
        string m_url, m_key;

        public bool WebApiEnabled { get { Uri uri; return Uri.TryCreate(m_url, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttp; } }
        public string WebApi { get { return m_url; } }
        public string WebApiKey { get { return m_key; } }

        public WinterOptions(Options options)
        {
            m_channel = options.Channel.ToLower();

            IniSection section = options.IniReader.GetSectionByName("winter");
            if (section == null)
                return;

            section.GetValue("WebApi", ref m_url);
            section.GetValue("ApiKey", ref m_key);
        }


        public HttpWebRequest CreateGetRequest(string api, bool compressed, string values = null)
        {
            values = string.IsNullOrWhiteSpace(values) ? "" : "&" + values;
            string url = string.Format("{0}/getpoints.php?CHANNEL={1}{2}", m_url, m_channel, values);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.KeepAlive = false;
            
            if (compressed)
                request.ContentType = "application/x-gzip";

            if (!string.IsNullOrWhiteSpace(m_key))
                request.Headers.Add("APIKey", m_key);

            return request;
        }

        public HttpWebRequest CreatePostRequest(string api, bool compressed, string values = null)
        {
            values = string.IsNullOrWhiteSpace(values) ? "" : "&" + values;
            string url = string.Format("{0}/{1}?CHANNEL={2}{3}", m_url, api, m_channel, values);

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.KeepAlive = false;

            if (compressed)
                request.ContentType = "application/x-gzip";

            if (!string.IsNullOrWhiteSpace(m_key))
                request.Headers.Add("APIKey", m_key);

            return request;
        }
    }
}
