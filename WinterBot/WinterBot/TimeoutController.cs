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

namespace Winter
{
    public class TimeoutController
    {
        const string s_urlExtensions = "arpa,com,edu,firm,gov,int,mil,mobi,nato,net,nom,org,store,web,me,ac,ad,ae,af,ag,ai,al,am,an,ao,aq,ar,as,at,au,aw,az,ba,bb,bd,be,bf,bg,bh,bi,bj,bm,bn,bo,br,bs,bt,bv,bw,by,bz,ca,cc,cf,cg,ch,ci,ck,cl,cm,cn,co,cr,cs,cu,cv,cx,cy,cz,de,dj,dk,dm,do,dz,ec,ee,eg,eh,er,es,et,eu,fi,fj,fk,fm,fo,fr,fx,ga,gb,gd,ge,gf,gh,gi,gl,gm,gn,gp,gq,gr,gs,gt,gu,gw,gy,hk,hm,hn,hr,ht,hu,id,ie,il,in,io,iq,ir,is,it,jm,jo,jp,ke,kg,kh,ki,km,kn,kp,kr,kw,ky,kz,la,lb,lc,li,lk,lr,ls,lt,lu,lv,ly,ma,mc,md,mg,mh,mk,ml,mm,mn,mo,mp,mq,mr,ms,mt,mu,mv,mw,mx,my,mz,na,nc,ne,nf,ng,ni,nl,no,np,nr,nt,nu,nz,om,pa,pe,pf,pg,ph,pk,pl,pm,pn,pr,pt,pw,py,qa,re,ro,ru,rw,sa,sb,sc,sd,se,sg,sh,si,sj,sk,sl,sm,sn,so,sr,st,su,sv,sy,sz,tc,td,tf,tg,th,tj,tk,tm,tn,to,tp,tr,tt,tv,tw,tz,ua,ug,uk,um,us,uy,uz,va,vc,ve,vg,vi,vn,vu,wf,ws,ye,yt,yu,za,zm,zr,zw";
        WinterBot m_winterBot;

        HashSet<TwitchUser> m_permit = new HashSet<TwitchUser>();

        Regex m_url = new Regex(@"([\w-]+\.)+([\w-]+)(/[\w-./?%&=]*)?", RegexOptions.IgnoreCase);
        List<Regex> m_urlWhitelist;
        List<Regex> m_urlBlacklist;
        List<Regex> m_urlBanlist;
        HashSet<string> m_urlExtensions;
        
        private HashSet<string> m_defaultImageSet;
        private Dictionary<int, HashSet<string>> m_imageSets;

        Dictionary<TwitchUser, TimeoutCount> m_timeouts = new Dictionary<TwitchUser, TimeoutCount>();
        Options m_options;

        public TimeoutController(WinterBot bot)
        {
            ThreadPool.QueueUserWorkItem(LoadEmoticons);
            LoadOptions(bot.Options);

            m_winterBot = bot;
            m_winterBot.MessageReceived += CheckMessage;
        }


        void LoadOptions(Options options)
        {
            m_options = options;

            // Load url lists
            m_urlWhitelist = new List<Regex>(m_options.UrlWhitelist.Select(s=>new Regex(s, RegexOptions.IgnoreCase)));
            m_urlBlacklist = new List<Regex>(m_options.UrlBlacklist.Select(s => new Regex(s, RegexOptions.IgnoreCase)));
            m_urlBanlist = new List<Regex>(m_options.UrlBanlist.Select(s => new Regex(s, RegexOptions.IgnoreCase)));

            // Load URL extensions
            m_urlExtensions = new HashSet<string>(s_urlExtensions.Split(','));
        }
        
        [BotCommand(AccessLevel.Mod, "deny")]
        public void Deny(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            value = value.Trim();

            if (!TwitchUsers.IsValidUserName(value))
            {
                sender.SendResponse("{0}: Usage: !deny [user]", user.Name);
                return;
            }

            user = sender.Users.GetUser(value);
            if (sender.CanUseCommand(user, AccessLevel.Regular))
                return;

            if (m_permit.Contains(user))
                m_permit.Remove(user);
            else
                sender.SendResponse("{0}: You are not allowed to post a link.  You couldn't anyway, but someone thought you could use a reminder.", user.Name);
        }

        [BotCommand(AccessLevel.Mod, "permit")]
        public void Permit(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            Debug.Assert(m_winterBot == sender);

            value = value.Trim().ToLower();
            if (!TwitchUsers.IsValidUserName(value))
            {
                m_winterBot.WriteDiagnostic(DiagnosticFacility.UserError, "{0}: Invalid username '{1}.", cmd, value);
                return;
            }

            m_permit.Add(sender.Users.GetUser(value));
            m_winterBot.SendResponse("{0} -> {1} has been granted permission to post a single link.", user.Name, value);
        }


        public void CheckMessage(WinterBot sender, TwitchUser user, string text)
        {
            if (user.IsSubscriber || user.IsModerator || sender.IsRegular(user))
                return;

            string clearReason = null;

            List<string> urls;
            if (m_options.TimeoutUrls && HasUrls(text, out urls))
            {
                // Check bans.
                if (MatchesAny(urls, m_urlBanlist))
                {
                    m_winterBot.Ban(user);
                    if (!string.IsNullOrEmpty(m_options.UrlBanMessage))
                        sender.TimeoutMessage("{0}: {1}", user.Name, m_options.UrlBanMessage);

                    m_winterBot.WriteDiagnostic(DiagnosticFacility.Ban, "Banned {0} for {1}.", user.Name, string.Join(", ", urls));
                }
                else if (!MatchesAll(urls, m_urlWhitelist) || MatchesAny(urls, m_urlBlacklist))
                {
                    if (m_permit.Contains(user))
                        m_permit.Remove(user);
                    else
                        clearReason = m_options.UrlTimeoutMessage;
                }
            }
            else if (m_options.TimeoutSpecialChars && HasSpecialCharacter(text))
            {
                clearReason = m_options.SpecialCharMessage;
            }
            else if (m_options.TimeoutCaps && TooManyCaps(text))
            {
                clearReason = m_options.CapsTimeoutMesssage;
            }
            else if (m_options.TimeoutEmotes && TooManyEmotes(user, text))
            {
                clearReason = m_options.EmoteMessage;
            }
            else if (m_options.TimeoutLongMessages && MessageTooLong(user, text))
            {
                clearReason = m_options.LongMessageTimeout;
            }

            if (clearReason != null)
                ClearChat(sender, user, clearReason);
        }

        private bool MessageTooLong(TwitchUser user, string text)
        {
            if (m_options.MaxMessageLength <= 0)
                return false;

            return text.Length > m_options.MaxMessageLength;
        }

        private void ClearChat(WinterBot sender, TwitchUser user, string clearReason)
        {
            bool shouldMessage = !string.IsNullOrEmpty(clearReason);
            var now = DateTime.Now;
            TimeoutCount timeout;
            if (!m_timeouts.TryGetValue(user, out timeout))
            {
                timeout = m_timeouts[user] = new TimeoutCount(now);
            }
            else
            {
                shouldMessage &= (DateTime.Now > timeout.LastTimeout) && (DateTime.Now - timeout.LastTimeout).TotalMinutes > 60;

                int curr = timeout.Count;
                int diff = (int)(now - timeout.LastTimeout).TotalMinutes / 15;

                if (diff > 0)
                    curr -= diff;

                if (curr < 0)
                    curr = 0;

                timeout.Count = curr + 1;
            }

            timeout.LastTimeout = now;

            int duration = 0;
            switch (timeout.Count)
            {
                case 1:
                case 2:
                    if (shouldMessage)
                        sender.Send(MessageType.Timeout, "{0}: {1} (This is not a timeout.)", user.Name, clearReason);

                    sender.ClearChat(user);
                    break;

                case 3:
                    duration = 5;
                    sender.Send(MessageType.Timeout, "{0}: {1} ({2} minute timeout.)", user.Name, clearReason, duration);
                    sender.Timeout(user, duration * 60);
                    timeout.LastTimeout = now.AddMinutes(duration);
                    break;

                case 4:
                    duration = 10;
                    sender.Send(MessageType.Timeout, "{0}: {1} ({2} minute timeout.)", user.Name, clearReason, duration);
                    sender.Timeout(user, duration * 60);
                    timeout.LastTimeout = now.AddMinutes(duration);
                    break;

                default:
                    Debug.Assert(timeout.Count > 0);
                    sender.Send(MessageType.Timeout, "{0}: {1} (8 hour timeout.)", user.Name, clearReason);
                    sender.Timeout(user, 8 * 60 * 60);
                    timeout.LastTimeout = now.AddHours(8);
                    break;
            }
        }


        class TimeoutCount
        {
            public TimeoutCount(DateTime time)
            {
                LastTimeout = time;
                Count = 1;
            }

            public DateTime LastTimeout { get; set; }
            public int Count { get; set; }
        }

        private static bool MatchesAny(List<string> urls, List<Regex> regexes)
        {
            return urls.Any(url => regexes.Any(regex => regex.IsMatch(url)));
        }

        private static bool MatchesAll(List<string> urls, List<Regex> regexes)
        {
            return urls.All(url => regexes.Any(regex => regex.IsMatch(url)));
        }

        private bool TooManyEmotes(TwitchUser user, string message)
        {
            int count = 0;

            if (m_defaultImageSet != null)
            {
                foreach (string item in m_defaultImageSet)
                {
                    count += CountEmote(message, item);
                    if (count > m_options.EmoteMax)
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
                        if (count > m_options.EmoteMax)
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
            int maxCaps = m_options.MaxCaps;
            int capsPercent = m_options.MaxCapsPercent;
            if (maxCaps <= 0 || capsPercent <= 0)
                return false;

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
            
            if (maxCaps > 0 && total < maxCaps)
                return false;

            int percent = 100 * upper / total;
            if (capsPercent > 0 && percent < capsPercent)
                return false;

            return true;
        }


        bool HasSpecialCharacter(string str)
        {
            for (int i = 0; i < str.Length; ++i)
                if (!Allowed(str[i]))
                    return true;

            return false;
        }

        bool Allowed(char c)
        {
            if (c < 255)
                return true;

            // punctuation block
            if (0x2010 <= c && c <= 0x2049)
                return true;

            return c == '♥' || c == '…' || c == '€' || (m_options.AllowKorean && IsKoreanCharacter(c));
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

        bool HasUrls(string str, out List<string> urls)
        {
            urls = null;

            var matches = m_url.Matches(str);
            if (matches.Count == 0)
                return false;

            urls = new List<string>(matches.Count);
            foreach (Match match in matches)
            {
                var groups = match.Groups;
                if (m_urlExtensions.Contains(groups[groups.Count - 2].Value))
                    urls.Add(groups[0].Value);
            }
            
            return urls.Count > 0;
        }


        private void LoadEmoticons(object state)
        {
            var req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(@"https://api.twitch.tv/kraken/chat/emoticons");
            req.UserAgent = "WinterBot/0.0.1.0";
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