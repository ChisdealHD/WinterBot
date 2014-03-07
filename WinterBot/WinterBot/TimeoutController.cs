using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WinterBot
{
    public class TimeoutController
    {
        private WinterBot m_winterBot;
        HashSet<string> m_permit = new HashSet<string>();
        HashSet<string> m_allowedUrls = new HashSet<string>();
        HashSet<string> m_urlExtensions;
        private HashSet<string> m_defaultImageSet;
        private Dictionary<int, HashSet<string>> m_imageSets;
        Regex m_url = new Regex(@"([\w-]+\.)+([\w-]+)(/[\w- ./?%&=]*)?", RegexOptions.IgnoreCase);
        Regex m_banUrlRegex = new Regex(@"(slutty)|(naked)-[a-zA-Z0-9]+\.com", RegexOptions.IgnoreCase);

        int m_maxCaps = 16;
        int m_capsPercent = 70;
        int m_maxEmotes = 3;

        public TimeoutController(WinterBot bot)
        {
            ThreadPool.QueueUserWorkItem(LoadEmoticons);
            LoadOptions(bot.Options.RawIniData);

            m_winterBot = bot;
            m_winterBot.MessageReceived += CheckMessage;
        }


        void LoadOptions(IniReader options)
        {
            // Load url whitelist
            section = options.GetSectionByName("whitelist");
            if (section != null)
                m_allowedUrls = new HashSet<string>(section.EnumerateRawStrings());

            // Load URL extensions
            var section = options.GetSectionByName("chat");
            if (section != null)
            {
                var exts = section.GetValue("urlExtensions");
                if (exts != null)
                    m_urlExtensions = new HashSet<string>(exts.Split(','));
            }

            if (m_urlExtensions == null)
            {
                string[] tmp = { "net", "com", "org" };
                m_urlExtensions = new HashSet<string>(tmp);
            }

            // Load timeout variables
            section.GetValue("MaxCaps", ref m_maxCaps);
            section.GetValue("MaxCapsPercent", ref m_capsPercent);
            section.GetValue("MaxEmotes", ref m_maxEmotes);
        }


        [BotCommand(AccessLevel.Mod, "permit")]
        public void Permit(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            Debug.Assert(m_winterBot == sender);

            value = value.Trim().ToLower();

            var userData = m_winterBot.UserData;
            if (!userData.IsValidUserName(value))
            {
                m_winterBot.WriteDiagnostic(DiagnosticLevel.Notify, "{0}: Invalid username '{1}.", cmd, value);
                return;
            }

            m_permit.Add(value);
            m_winterBot.SendMessage("{0} -> {1} has been granted permission to post a single link.", user.Name, value);
        }


        public void CheckMessage(WinterBot sender, TwitchUser user, string text)
        {
            if (user.IsSubscriber || user.IsModerator || sender.IsRegular(user))
                return;

            if (HasSpecialCharacter(text))
            {
                m_winterBot.SendMessage(string.Format("{0}: Sorry, no special characters allowed to keep the dongers to a minimum. (This is not a timeout.)", user.Name));
                user.ClearChat();
                return;
            }

            if (TooManyCaps(text))
            {
                m_winterBot.SendMessage(string.Format("{0}: Sorry, please don't spam caps. (This is not a timeout.)", user.Name));
                user.ClearChat();
                return;
            }

            if (TooManyEmotes(user, text))
            {
                m_winterBot.SendMessage(string.Format("{0}: Sorry, please don't spam emotes. (This is not a timeout.)", user.Name));
                user.ClearChat();
                return;
            }

            string url;
            if (HasUrl(text, out url))
            {
                text = text.ToLower();
                url = url.ToLower();
                if (!m_allowedUrls.Contains(url) || (url.Contains("teamliquid") && (text.Contains("userfiles") || text.Contains("image") || text.Contains("profile"))))
                {
                    if (m_banUrlRegex.IsMatch(url) || url.Contains("codes4free.net") || url.Contains("vine4you.com") || url.Contains("prizescode.net"))
                    {
                        m_winterBot.SendMessage(string.Format("{0}: Banned.", user.Name));
                        user.Ban();
                    }
                    else
                    {
                        m_winterBot.SendMessage(string.Format("{0}: Only subscribers are allowed to post links. (This is not a timeout.)", user.Name));
                        user.ClearChat();
                    }

                    return;
                }
            }
        }

        private bool TooManyEmotes(TwitchUser user, string message)
        {
            int count = 0;

            if (m_defaultImageSet != null)
            {
                foreach (string item in m_defaultImageSet)
                {
                    count += CountEmote(message, item);
                    if (count > m_maxEmotes)
                        return true;
                }
            }

            int[] userSets = user.IconSet;
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
                        if (count > m_maxEmotes)
                            return true;
                    }
                }
            }

            return false;
        }

        private int CountEmote(string message, string item)
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

        private bool TooManyCaps(string message)
        {
            int upper = 0;
            int lower = 0;

            foreach (char c in message)
            {
                if ('a' <= c && c <= 'z')
                    lower++;
                else if ('A' <= c && c <= 'Z')
                    upper++;
            }

            int total = lower + upper;
            if (total < m_maxCaps)
                return false;

            int percent = 100 * upper / total;
            if (percent < m_capsPercent)
                return false;

            return true;
        }


        static bool HasSpecialCharacter(string str)
        {
            for (int i = 0; i < str.Length; ++i)
                if (!Allowed(str[i]))
                    return true;

            return false;
        }

        static bool Allowed(char c)
        {
            if (c < 255)
                return true;

            // punctuation block
            if (0x2010 <= c && c <= 0x2049)
                return true;

            return c == '♥' || c == '…' || c == '€' || IsKoreanCharacter(c);
        }

        static bool IsKoreanCharacter(char c)
        {
            return (0xac00 <= c && c <= 0xd7af) ||
                (0x1100 <= c && c <= 0x11ff) ||
                (0x3130 <= c && c <= 0x318f) ||
                (0x3200 <= c && c <= 0x32ff) ||
                (0xa960 <= c && c <= 0xa97f) ||
                (0xd7b0 <= c && c <= 0xd7ff) ||
                (0xff00 <= c && c <= 0xffef);
        }

        bool HasUrl(string str, out string url)
        {
            url = null;
            var match = m_url.Match(str);

            if (!match.Success)
                return false;

            var groups = match.Groups;
            if (!m_urlExtensions.Contains(groups[groups.Count - 2].Value))
                return false;

            url = groups[1].Value + groups[2].Value;
            return true;
        }


        private void LoadEmoticons(object state)
        {
            var req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(@"https://api.twitch.tv/kraken/chat/emoticons");
            req.UserAgent = "Question Grabber Bot/0.0.0.1";
            var response = req.GetResponse();
            var fromStream = response.GetResponseStream();

            StreamReader reader = new StreamReader(fromStream);
            string data = reader.ReadToEnd();

            TwitchEmoticonResponse emotes = JsonConvert.DeserializeObject<TwitchEmoticonResponse>(data);

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

            m_defaultImageSet = defaultSet;
            m_imageSets = imageSets;
        }
    }
}