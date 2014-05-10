using System;
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
    class HttpManager
    {
        public WinterOptions Options { get; internal set; }
        public string ApiKey { get { return Options.WebApiKey; } }

        public HttpManager(WinterOptions options)
        {
            Options = options;
        }

        public void PostAsync(string api, StringBuilder data)
        {
            PostAsync(api, null, data);
        }

        public Task PostAsync(string api, string parameters, StringBuilder data)
        {
            string url = GetUrl(api, parameters);
            HttpPost post = new HttpPost(this, url, data.Length > 512, data);
            Task t = new Task(post.Go);
            t.Start();

            return t;
        }

        public void GetAsync(string api, Action<Stream> callback)
        {
            GetAsync(api, null, callback);
        }

        public Task GetAsync(string api, string parameters, Action<Stream> callback)
        {
            string url = GetUrl(api, parameters);

            HttpGet get = new HttpGet(this, url, callback);
            Task t = new Task(get.Go);
            t.Start();

            return t;
        }

        public string GetUrl(string api, string values=null)
        {
            values = string.IsNullOrWhiteSpace(values) ? "" : "&" + values;
            return string.Format("{0}/{1}?CHANNEL={2}{3}", Options.WebApi, api, Options.Channel, values);
        }

        internal bool ParsePostResult(HttpWebRequest request, out string response)
        {
            var result = request.GetResponse();

            Stream stream = result.GetResponseStream();
            if (result.ContentType.Equals("application/x-gzip", StringComparison.CurrentCultureIgnoreCase))
                stream = new GZipStream(stream, CompressionMode.Decompress);
            
            response = stream.ReadToEnd();
            return response == "ok" || response == "complete";
        }
    }


    class HttpGet
    {
        string m_url;
        Action<Stream> m_callback;
        HttpManager m_http;

        public HttpGet(HttpManager http, string url, Action<Stream> callback)
        {
            m_http = http;
            m_url = url;
            m_callback = callback;
        }

        internal void Go()
        {
            try
            {
                HttpWebRequest request = CreateGetRequest(m_url);
                WebResponse response = request.GetResponse();
                m_callback(response.GetResponseStream());

                WinterSource.Log.GetHttp(m_url, response.ContentLength, "ok");
            }
            catch (Exception e)
            {
                m_callback(null);
                WinterSource.Log.GetHttp(m_url, 0, string.Format("Exception: {0} Message: {1}", e.GetType().Name, e.Message));
            }
        }

        HttpWebRequest CreateGetRequest(string url)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "GET";
            request.KeepAlive = false;

            var apiKey = m_http.ApiKey;
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.Add("APIKey", apiKey);

            return request;
        }
    }


    class HttpPost
    {
        public delegate void HttpResult(Stream response);

        HttpManager m_http;
        string m_url;
        bool m_compress;
        StringBuilder m_data;

        public HttpPost(HttpManager manager, string url, bool compress, StringBuilder data)
        {
            m_http = manager;
            m_url = url;
            m_compress = compress;
            m_data = data;
        }

        public void Go()
        {
            bool complete = false;
            do
            {
                try
                {
                    HttpWebRequest request = CreatePostRequest(m_url, m_compress);

                    Stream requestStream = request.GetRequestStream();
                    if (m_compress)
                    {
                        using (GZipStream gzip = new GZipStream(requestStream, CompressionLevel.Optimal))
                        using (StreamWriter stream = new StreamWriter(gzip))
                            stream.Write(m_data.ToString());
                    }
                    else
                    {
                        using (StreamWriter stream = new StreamWriter(requestStream))
                            stream.Write(m_data.ToString());
                    }

                    string response;
                    complete = m_http.ParsePostResult(request, out response);
                    WinterSource.Log.PostHttp(m_url, complete ? "ok" : response);
                }
                catch (Exception e)
                {
                    WinterSource.Log.PostHttp(m_url, string.Format("Exception: {0} Message: {1}", e.GetType().Name, e.Message));
                }

                if (!complete)
                    Thread.Sleep(10000);
            } while (!complete);
        }

        HttpWebRequest CreatePostRequest(string url, bool compressed)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.KeepAlive = false;

            if (compressed)
                request.ContentType = "application/x-gzip";

            if (!string.IsNullOrWhiteSpace(m_http.ApiKey))
                request.Headers.Add("APIKey", m_http.ApiKey);

            return request;
        }
    }
}
