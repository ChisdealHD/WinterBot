using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using WinterBotLogging;

namespace Winter
{
    public class TwitchHttp
    {
        public static TwitchHttp Instance = new TwitchHttp();

        Thread m_thread;
        Thread m_followerThread;
        string m_userAgent;

        HashSet<string> m_channelData = new HashSet<string>();
        Dictionary<string, DateTime> m_channelFollowers = new Dictionary<string, DateTime>();
        HashSet<int> m_loadedSets = new HashSet<int>();

        object m_sync = new object();

        public TwitchImageSet ImageSet { get; private set; }
        public string EmoticonFolder { get; private set; }

        public event Action<string, List<TwitchChannelResponse>> ChannelDataReceived;
        public event Action<string, IEnumerable<string>> UserFollowed;

        string m_cacheFolder;


        TwitchHttp()
        {
            m_thread = new Thread(ThreadProc);
            m_thread.Name = "Twitch WebAPI Thread";
            m_thread.Start();

            string botDll = typeof(WinterBot).Assembly.Location;
            var verInfo = FileVersionInfo.GetVersionInfo(botDll);
            m_userAgent = string.Format("WinterBot/{0}.{1}.0.0", verInfo.ProductMajorPart, verInfo.FileMinorPart);

            SetEmoticonCache(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Twitch"));
        }

        public void SetEmoticonCache(string folder)
        {
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            m_cacheFolder = folder;
        }
        
        public void EnsureEmoticonsLoaded()
        {
            lock (m_sync)
            {
                if (ImageSet == null || string.IsNullOrEmpty(m_cacheFolder))
                    return;

                if (!m_loadedSets.Contains(-1))
                {
                    ThreadPool.QueueUserWorkItem(DownloadSet, new Tuple<IEnumerable<TwitchEmoticon>, int>(ImageSet.DefaultSet, -1));
                    m_loadedSets.Add(-1);
                }
            }
        }

        public void EnsureEmoticonsLoaded(int set)
        {
            lock (m_sync)
            {
                if (ImageSet == null || string.IsNullOrEmpty(m_cacheFolder))
                    return;

                if (!m_loadedSets.Contains(-1))
                {
                    ThreadPool.QueueUserWorkItem(DownloadSet, new Tuple<IEnumerable<TwitchEmoticon>, int>(ImageSet.DefaultSet, -1));
                    m_loadedSets.Add(-1);
                }

                if (!m_loadedSets.Contains(set))
                {
                    ThreadPool.QueueUserWorkItem(DownloadSet, new Tuple<IEnumerable<TwitchEmoticon>, int>(ImageSet.GetEmoticons(set), set));
                    m_loadedSets.Add(set);
                }
            }
        }

        void DownloadSet(object param)
        {
            var tmp = (Tuple<IEnumerable<TwitchEmoticon>, int>)param;
            IEnumerable<TwitchEmoticon> set = tmp.Item1;
            int id = tmp.Item2;

            WebClient client = new WebClient();
            foreach (var emote in set)
            {
                if (!string.IsNullOrWhiteSpace(emote.LocalFile))
                    continue;

                if (File.Exists(emote.LocalFile))
                    continue;

                try
                {
                    client.DownloadFile(emote.Url, emote.LocalFile);
                }
                catch (Exception e)
                {
                    
                }
            }
        }

        public void PollChannelData(string channel)
        {
            lock (m_sync)
                m_channelData.Add(channel.ToLower());
        }

        public void PollFollowers(string channel)
        {
            lock (m_sync)
            {
                if (m_followerThread == null)
                {
                    m_followerThread = new Thread(FollowerThreadProc);
                    m_followerThread.Name = "Twitch WebAPI Follower Thread";
                    m_followerThread.Start();
                }

                m_channelFollowers[channel.ToLower()] = DateTime.MinValue;
            }
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

        public void StopPolling()
        {
            lock (m_sync)
            {
                m_channelData.Clear();
                m_channelFollowers.Clear();
            }
        }


        private void FollowerThreadProc()
        {
            while (true)
            {
                foreach (var channel in GetFollowChannels())
                    PollFollower(channel);

                Thread.Sleep(15000);
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

                foreach (var channel in GetChannels())
                {
                    PollChannel(channel);
                    Thread.Sleep(15000);
                }
                
                totalTime = totalTime.Subtract(DateTime.Now - start);
                if (totalTime.TotalMilliseconds >= minSleep)
                    Thread.Sleep((int)totalTime.TotalMilliseconds);
            }
        }

        List<string> GetFollowChannels()
        {
            lock (m_sync)
                return new List<string>(m_channelFollowers.Keys);
        }

        List<string> GetChannels()
        {
            lock (m_sync)
                return new List<string>(m_channelData);
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

                lock (m_sync)
                    m_channelFollowers[channel] = DateTime.Parse(follow.follows[0].created_at);
            }
            else
            {
                DateTime curr = DateTime.MinValue;
                bool first = true;
                int count = 0;
                int limit = 25;
                bool done = false;

                List<string> followers = new List<string>();
                string url = string.Format("https://api.twitch.tv/kraken/channels/{0}/follows?direction=desc&offset={1}&limit={2}", channel, count, limit);
                do
                {
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
                        {
                            done = true;
                            break;
                        }

                        followers.Add(follow.user.name);
                        first = false;
                    }

                    done |= follows.follows.Count == 0;
                    url = follows._links.next;
                } while (!done);

                if (!first)
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
            if (channels != null)
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

            List<TwitchEmoticon> defaultSet = new List<TwitchEmoticon>();
            List<List<TwitchEmoticon>> imageSets = new List<List<TwitchEmoticon>>(4096);

            foreach (var emote in emotes.emoticons)
            {
                foreach (var image in emote.images)
                {
                    if (image.emoticon_set == null)
                    {
                        defaultSet.Add(new TwitchEmoticon(m_cacheFolder, emote, image, true));
                    }
                    else
                    {
                        int setId = (int)image.emoticon_set;

                        Debug.Assert(setId <= 6000);
                        if (setId > 6000)
                            continue;

                        while (setId >= imageSets.Count)
                            imageSets.Add(null);

                        List<TwitchEmoticon> set = imageSets[setId];
                        if (set == null)
                            imageSets[setId] = set = new List<TwitchEmoticon>();

                        set.Add(new TwitchEmoticon(m_cacheFolder, emote, image, false));
                    }
                }
            }

            ImageSet = new TwitchImageSet(defaultSet, imageSets);
        }

        internal void StopPollingFollowers(string m_channel)
        {
            lock (m_sync)
                m_channelFollowers.Clear();
        }
    }

    public class TwitchEmoticon
    {
        public string Name { get; set; }

        public int? Width { get; set; }

        public int? Height { get; set; }

        public string Url { get; set; }

        public string LocalFile { get; private set; }

        public bool Default { get; set; }

        Regex m_reg;

        internal TwitchEmoticon(string cache, Emoticon e, JsonImage i, bool deflt)
        {
            Name = e.regex.Trim();
            Width = i.width;
            Height = i.height;
            Url = i.url;
            Default = deflt;

            LocalFile = Path.Combine(cache, GetUrlFilename(i.url));

            if (Name.IsRegex())
            {
                Name = Name.Replace(@"&gt\;", ">").Replace(@"&lt\;", "<");
                try
                {
                    m_reg = new Regex(Name);
                }
                catch
                {
                }
            }
        }
        public IEnumerable<Tuple<TwitchEmoticon, int, int>> Find(string str)
        {
            if (m_reg != null)
            {
                var matches = m_reg.Matches(str);
                foreach (Match match in matches)
                    yield return new Tuple<TwitchEmoticon, int, int>(this, match.Index, match.Length);
            }
            else
            {
                int i = -1;

                while (i + Name.Length <= str.Length)
                {
                    i = str.IndexOf(Name, i + 1);
                    if (i == -1)
                        break;

                    yield return new Tuple<TwitchEmoticon, int, int>(this, i, Name.Length);
                }
            }
        }

        public override string ToString()
        {
            return Name;
        }

        private static string GetUrlFilename(string url)
        {
            string filename = string.Empty;
            int i = url.LastIndexOf('/');
            if (i > -1)
                filename = url.Substring(i + 1);
            return filename;
        }

    }

    public class TwitchImageSet
    {
        List<TwitchEmoticon> m_defaultImageSet;
        List<List<TwitchEmoticon>> m_imageSets;

        public IEnumerable<TwitchEmoticon> DefaultSet { get { return m_defaultImageSet; } }

        public IEnumerable<int> Sets
        {
            get
            {
                for (int i = 0; i < m_imageSets.Count; ++i)
                {
                    if (m_imageSets[i] != null)
                        yield return i;
                }
            }
        }

        public TwitchImageSet(List<TwitchEmoticon> defaultSet, List<List<TwitchEmoticon>> imageSets)
        {
            m_defaultImageSet = defaultSet;
            m_imageSets = imageSets;
        }
        
        public IEnumerable<Tuple<TwitchEmoticon, int, int>> Find(string str, int[] imageSets)
        {
            if (imageSets != null)
                foreach (int set in imageSets)
                    foreach (var emoticon in GetImageSet(set))
                        foreach (var found in emoticon.Find(str))
                            yield return found;

            foreach (var emoticon in m_defaultImageSet)
                foreach (var found in emoticon.Find(str))
                    yield return found;
        }

        public IEnumerable<TwitchEmoticon> GetEmoticons(int set)
        {
            return GetImageSet(set);
        }

        public bool TooManySymbols(string message, int max, int[] userSets)
        {
            int count = 0;

            if (m_defaultImageSet != null)
            {
                foreach (string item in m_defaultImageSet.Select(p=>p.Name))
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
                    foreach (string item in GetImageSet(setId).Select(p=>p.Name))
                    {
                        count += CountEmote(message, item);
                        if (count > max)
                            return true;
                    }
                }
            }

            return false;
        }

        static List<TwitchEmoticon> s_emptyList = new List<TwitchEmoticon>(0);
        private List<TwitchEmoticon> GetImageSet(int setId)
        {
            if (setId >= m_imageSets.Count)
                return s_emptyList;

            return m_imageSets[setId] ?? s_emptyList;
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
