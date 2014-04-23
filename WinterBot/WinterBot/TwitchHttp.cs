using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using WinterBotLogging;

namespace Winter
{
    class TwitchHttp
    {
        public static TwitchHttp Instance = new TwitchHttp();

        Thread m_thread;
        string m_userAgent;

        HashSet<string> m_channelData = new HashSet<string>();
        Dictionary<string, DateTime> m_channelFollowers = new Dictionary<string, DateTime>();

        object m_sync = new object();

        public TwitchImageSet ImageSet { get; private set; }

        public event Action<string, List<TwitchChannelResponse>> ChannelDataReceived;
        public event Action<string, IEnumerable<string>> UserFollowed;


        TwitchHttp()
        {
            m_thread = new Thread(ThreadProc);
            m_thread.Name = "Twitch WebAPI Thread";
            m_thread.Start();

            string botDll = typeof(WinterBot).Assembly.Location;
            var verInfo = FileVersionInfo.GetVersionInfo(botDll);
            m_userAgent = string.Format("WinterBot/{0}.{1}.0.0", verInfo.ProductMajorPart, verInfo.FileMinorPart);
        }

        public void PollChannelData(string channel)
        {
            lock (m_sync)
                m_channelData.Add(channel.ToLower());
        }

        public void PollFollowers(string channel)
        {
            lock (m_sync)
                m_channelFollowers[channel.ToLower()] = DateTime.MinValue;
        }

        public void StopPolling(string channel)
        {
            channel = channel.ToLower();

            lock (m_sync)
            {
                if (m_channelData.Contains(channel))
                    m_channelData.Remove(channel);

                if (m_channelFollowers.ContainsKey(channel))
                    m_channelFollowers.Remove(channel);
            }
        }

        private void ThreadProc()
        {
            const int minSleep = 250;
            while (true)
            {
                TimeSpan totalTime = new TimeSpan(0, 1, 0);
                DateTime start = DateTime.Now;

                if (ImageSet == null)
                {
                    LoadEmoticons();
                    Thread.Sleep(15000);
                }

                string[] channels;
                lock (m_sync)
                    channels = m_channelData.ToArray();

                foreach (var channel in channels)
                {
                    PollChannel(channel);
                    Thread.Sleep(15000);
                }

                lock (m_sync)
                    channels = m_channelFollowers.Keys.ToArray();

                foreach (var channel in channels)
                {
                    PollFollower(channel);
                    Thread.Sleep(15000);
                }

                totalTime = totalTime.Subtract(DateTime.Now - start);
                if (totalTime.TotalMilliseconds >= minSleep)
                    Thread.Sleep((int)totalTime.TotalMilliseconds);
            }
        }

        private void PollFollower(string channel)
        {
            var evt = UserFollowed;
            if (evt == null)
                return;

            DateTime m_lastFollow;
            lock (m_sync)
                if (!m_channelFollowers.TryGetValue(channel, out m_lastFollow))
                    m_lastFollow = DateTime.MinValue;

            // Check followers
            if (m_lastFollow == DateTime.MinValue)
            {
                string url = string.Format("https://api.twitch.tv/kraken/channels/{0}/follows?direction=desc&limit=1&offset=0", channel);
                JsonFollows follows = GetUrl<JsonFollows>(GetUrl(url));

                var follow = JsonConvert.DeserializeObject<JsonFollows>(GetUrl(url));

                m_lastFollow = DateTime.Parse(follow.follows[0].created_at);
            }
            else
            {
                DateTime curr = DateTime.MinValue;
                bool first = false;
                int count = 0;
                int limit = 25;

                List<string> followers = new List<string>();
                do
                {
                    if (!first)
                        Thread.Sleep(15000);

                    string url = string.Format("https://api.twitch.tv/kraken/channels/{0}/follows?direction=desc&offset={1}&limit={2}", channel, count, limit);
                    count += limit;

                    JsonFollows follows = GetUrl<JsonFollows>(url);
                    if (follows == null)
                        break;
                    foreach (var follow in follows.follows)
                    {
                        DateTime last = DateTime.Parse(follow.created_at);
                        if (first)
                            curr = last;

                        if (last <= m_lastFollow)
                            break;

                        followers.Add(follow.user.name);
                        first = false;
                    }
                } while (true);

                if (first)
                    lock (m_sync)
                        m_channelFollowers[channel] = curr;

                evt(channel, followers);
            }
        }

        private string GetUrl(string url)
        {
            try
            {
                var req = (HttpWebRequest)HttpWebRequest.Create(url);
                req.UserAgent = m_userAgent;
                req.KeepAlive = false;

                return req.GetResponse().GetResponseStream().ReadToEnd();
            }
            catch (Exception e)
            {
                WinterBotSource.Log.TwitchWebApiError(url, e.ToString());
            }

            return null;
        }

        private T GetUrl<T>(string url)
        {
            try
            {
                var req = (HttpWebRequest)HttpWebRequest.Create(url);
                req.UserAgent = m_userAgent;

                string result = req.GetResponse().GetResponseStream().ReadToEnd();
                if (result != null)
                    return JsonConvert.DeserializeObject<T>(result);
            }
            catch (Exception e)
            {
                WinterBotSource.Log.TwitchWebApiError(url, e.ToString());
            }

            return default(T);
        }

        private void PollChannel(string channel)
        {
            var evt = ChannelDataReceived;
            if (evt == null)
                return;

            // Check stream values
            string url = @"http://api.justin.tv/api/stream/list.json?channel=" + channel;
            List<TwitchChannelResponse> channels = GetUrl<List<TwitchChannelResponse>>(url);

            WinterBotSource.Log.CheckStreamStatus(channels != null);
            evt(channel, channels);
        }
        
        void LoadEmoticons()
        {

            TwitchEmoticonResponse emotes = null;
            try
            {
                var req = (HttpWebRequest)HttpWebRequest.Create(@"https://api.twitch.tv/kraken/chat/emoticons");
                req.UserAgent = "WinterBot/0.0.1.0";
                var response = req.GetResponse();
                var fromStream = response.GetResponseStream();

                StreamReader reader = new StreamReader(fromStream);
                string data = reader.ReadToEnd();

                emotes = JsonConvert.DeserializeObject<TwitchEmoticonResponse>(data);
            }
            catch (Exception e)
            {
                WinterBotSource.Log.TwitchWebApiError("emoticon", e.ToString());
                return;
            }

            HashSet<string> defaultSet = new HashSet<string>();
            Dictionary<int, HashSet<string>> imageSets = new Dictionary<int, HashSet<string>>();

            foreach (var emote in emotes.emoticons)
            {
                foreach (var image in emote.images)
                {
                    if (image.emoticon_set == null)
                    {
                        defaultSet.Add(emote.regex);
                    }
                    else
                    {
                        int setId = (int)image.emoticon_set;
                        HashSet<string> set;
                        if (!imageSets.TryGetValue(setId, out set))
                            imageSets[setId] = set = new HashSet<string>();

                        set.Add(emote.regex);
                    }
                }
            }

            ImageSet = new TwitchImageSet(defaultSet, imageSets);
        }

        internal void StopPollingFollowers(string m_channel)
        {
            throw new NotImplementedException();
        }
    }

    class TwitchImageSet
    {
        HashSet<string> m_defaultImageSet;
        Dictionary<int, HashSet<string>> m_imageSets;

        public TwitchImageSet(HashSet<string> defaultSet, Dictionary<int, HashSet<string>> imageSets)
        {
            m_defaultImageSet = defaultSet;
            m_imageSets = imageSets;
        }

        public bool TooManySymbols(string message, int max, int[] userSets)
        {
            int count = 0;

            if (m_defaultImageSet != null)
            {
                foreach (string item in m_defaultImageSet)
                {
                    count += CountEmote(message, item);
                    if (count > max)
                        return true;
                }
            }

            if (userSets != null && m_imageSets != null)
            {
                foreach (int setId in userSets)
                {
                    HashSet<string> imageSet;
                    if (!m_imageSets.TryGetValue(setId, out imageSet))
                        continue;

                    foreach (string item in imageSet)
                    {
                        count += CountEmote(message, item);
                        if (count > max)
                            return true;
                    }
                }
            }

            return false;
        }

        int CountEmote(string message, string item)
        {
            int count = 0;
            int i = message.IndexOf(item);
            while (i != -1)
            {
                count++;
                i = message.IndexOf(item, i + 1);
            }

            return count;
        }
    }
}
