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

        AutoResetEvent m_saveEvent = new AutoResetEvent(false);
        UserSet m_denyList;

        Regex m_url = new Regex(@"([\w-]+\.)+([\w-]+)(/[\w-./?%&=]*)?", RegexOptions.IgnoreCase);
        List<Regex> m_urlWhitelist;
        List<Regex> m_urlBlacklist;
        List<Regex> m_urlBanlist;
        HashSet<string> m_urlExtensions;
        
        HashSet<string> m_defaultImageSet;
        Dictionary<int, HashSet<string>> m_imageSets;

        Tuple<TwitchUser, string>[] m_lastMsgs = new Tuple<TwitchUser, string>[32];
        int m_currMsg;
        string m_clearMsg;
        int? m_spamTimeout = DefaultSpamTimeout;
        const int DefaultSpamTimeout = 600;

        Dictionary<TwitchUser, TimeoutCount> m_timeouts = new Dictionary<TwitchUser, TimeoutCount>();

        Options m_options;
        ChatOptions m_chatOptions;
        UrlTimeoutOptions m_urlOptions;
        CapsTimeoutOptions m_capsOptions;
        LengthTimeoutOptions m_lengthOptions;
        SymbolTimeoutOptions m_symbolOptions;
        EmoteTimeoutOptions m_emoteOptions;

        public TimeoutController(WinterBot bot)
        {
            m_winterBot = bot;
            LoadOptions(bot.Options);

            m_denyList = new UserSet(bot, "deny");

            m_winterBot.MessageReceived += CheckMessage;
            m_winterBot.ActionReceived += CheckAction;
            ThreadPool.QueueUserWorkItem(LoadEmoticons);
        }


        void LoadOptions(Options options)
        {
            m_options = options;
            m_chatOptions = options.ChatOptions;
            m_urlOptions = options.UrlOptions;
            m_capsOptions = options.CapsOptions;
            m_lengthOptions = options.LengthOptions;
            m_symbolOptions = options.SymbolOptions;
            m_emoteOptions = options.EmoteOptions;

            // Load url lists
            m_urlWhitelist = new List<Regex>(m_urlOptions.Whitelist.Select(s => new Regex(s, RegexOptions.IgnoreCase)));
            m_urlBlacklist = new List<Regex>(m_urlOptions.Blacklist.Select(s => new Regex(s, RegexOptions.IgnoreCase)));
            m_urlBanlist = new List<Regex>(m_urlOptions.Banlist.Select(s => new Regex(s, RegexOptions.IgnoreCase)));

            // Load URL extensions
            m_urlExtensions = new HashSet<string>(s_urlExtensions.Split(','));
        }

        [BotCommand(AccessLevel.Mod, "caps", "capsmode")]
        public void CapsMode(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            bool enable = false;

            value = value.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                sender.SendResponse("Caps protect is currently {0}.", m_capsOptions.Enabled ? "enabled" : "disabled");
                return;
            }
            else if (value.ParseBool(ref enable))
            {
                if (m_capsOptions.Enabled != enable)
                {
                    m_capsOptions.Enabled = enable;
                    string enableStr = m_capsOptions.Enabled ? "enabled" : "disabled";
                    sender.SendResponse("Caps protect is now {0}.", enableStr);
                    sender.WriteDiagnostic(DiagnosticFacility.ModeChange, "{0}: Changed caps mode to {1}.", user.Name, enableStr);
                }
            }
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

            var target = sender.Users.GetUser(value);
            if (target.IsModerator)
                return;

            if (m_permit.Contains(target))
                m_permit.Remove(target);

            m_denyList.Add(target);
            m_saveEvent.Set();
            sender.SendResponse("{0}: {1} is no longer allowed to post links.", user.Name, target.Name);
            
        }

        [BotCommand(AccessLevel.Mod, "permit", "allow")]
        public void Permit(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            Debug.Assert(m_winterBot == sender);

            value = value.Trim().ToLower();
            if (!TwitchUsers.IsValidUserName(value))
            {
                m_winterBot.WriteDiagnostic(DiagnosticFacility.UserError, "{0}: Invalid username '{1}.", cmd, value);
                return;
            }

            var target = sender.Users.GetUser(value);
            if (target.IsModerator)
                return;

            bool removed = m_denyList.TryRemove(target);
            if (removed)
            {
                m_saveEvent.Set();

                if (m_urlOptions.ShouldEnforce(target))
                    m_winterBot.SendResponse("{0}: {1} was removed from the deny list.", user.Name, target.Name);
                else
                    m_winterBot.SendResponse("{0}: {1} can now post links again.", user.Name, target.Name);
            }
            else
            {
                if (m_urlOptions.ShouldEnforce(target))
                {
                    m_permit.Add(target);
                    m_winterBot.SendResponse("{0} -> {1} has been granted permission to post a single link.", user.Name, target.Name);
                }
                else
                {
                    m_winterBot.SendResponse("{0}: {1} can posts links.", user.Name, target.Name);
                }
            }
        }

        [BotCommand(AccessLevel.Mod, "purge", "purgespam")]
        public void Purge(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            value = value.Trim().ToLower();
            if (string.IsNullOrWhiteSpace(value))
                return;

            var users = from msg in m_lastMsgs
                        where msg != null && msg.Item2 != null
                        where msg.Item2.Contains(value)
                        select msg.Item1;

            foreach (var usr in users.Distinct())
                sender.Timeout(usr, 1);
        }

        [BotCommand(AccessLevel.Mod, "clearspam", "clearban")]
        public void ClearSpam(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            m_clearMsg = null;
            m_spamTimeout = DefaultSpamTimeout;
        }

        [BotCommand(AccessLevel.Mod, "spam", "banmsg")]
        public void Spam(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            value = value.ToLower().Trim();

            string timeE = "-time=";
            bool ban = false;
            int timeout = DefaultSpamTimeout;
            if (value.Contains(' '))
            {
                string[] split = value.Split(new char[] { ' ' }, 2);
                if (split[0] == "-ban")
                {
                    ban = true;
                    value = split[1];
                }
                else if (split[0].Length > timeE.Length && split[0].StartsWith(timeE))
                {
                    string strtime = split[0].Substring(timeE.Length);
                    if (!int.TryParse(strtime, out timeout))
                    {
                        sender.SendResponse("Usage: !spam [-time=??] text to auto timeout");
                        return;
                    }

                    value = split[1];
                    if (timeout <= 0)
                        timeout = 1;
                }
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                // We'll just treat this as turning it off.
                m_clearMsg = null;
                m_spamTimeout = DefaultSpamTimeout;
                return;
            }
            else
            {
                m_clearMsg = value;
                m_spamTimeout = ban ? (int?)null : timeout;

                if (ban)
                    sender.SendResponse("Banning all messages containing '{0}'.", value);
                else if (timeout <= 1)
                    sender.SendResponse("All messages containing '{0}' will be purged.", value);
                else
                    sender.SendResponse("All messages containing '{0}' will receive a {1} second timeout.", value, timeout);
            }

            // Apply rule to recent messages
            HashSet<TwitchUser> timedOut = new HashSet<TwitchUser>();
            foreach (var msg in m_lastMsgs.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Item2)))
            {
                if (timedOut.Contains(msg.Item1))
                    continue;

                if (CheckAndTimeoutSpam(sender, msg.Item1, msg.Item2))
                    timedOut.Add(msg.Item1);
            }
        }

        void CheckAction(WinterBot bot, TwitchUser user, string text)
        {
            if (user.IsModerator)
                return;

            if (m_chatOptions.CheckFakeSubscribe && IsFakeSubscribe(text))
                ClearChat(bot, user, m_chatOptions.FakeSubscriberMessage);
            else
                CheckMessage(bot, user, text);
        }

        void CheckMessage(WinterBot bot, TwitchUser user, string text)
        {
            if (user.IsModerator)
                return;


            if (CheckAndTimeoutSpam(bot, user, text))
                return;

            string clearReason = null;

            List<string> urls;
            if (HasUrls(text, out urls))
            {
                // Check bans.
                if (MatchesAny(urls, m_urlBanlist))
                {
                    m_winterBot.Ban(user);
                    if (!string.IsNullOrEmpty(m_urlOptions.BanMessage))
                        bot.TimeoutMessage("{0}: {1}", user.Name, m_urlOptions.BanMessage);

                    m_winterBot.WriteDiagnostic(DiagnosticFacility.Ban, "Banned {0} for {1}.", user.Name, string.Join(", ", urls));
                }
                else if ((m_urlOptions.ShouldEnforce(user) || m_denyList.Contains(user)) && (!MatchesAll(urls, m_urlWhitelist) || MatchesAny(urls, m_urlBlacklist)))
                {
                    if (m_permit.Contains(user))
                        m_permit.Remove(user);
                    else
                        clearReason = m_urlOptions.Message;
                }
            }
            else if (m_symbolOptions.ShouldEnforce(user) && HasSpecialCharacter(text))
            {
                clearReason = m_symbolOptions.Message;
            }
            else if (m_capsOptions.ShouldEnforce(user) && TooManyCaps(user, text))
            {
                clearReason = m_capsOptions.Message;
            }
            else if (m_emoteOptions.ShouldEnforce(user) && TooManyEmotes(user, text))
            {
                clearReason = m_emoteOptions.Message;
            }
            else if (m_lengthOptions.ShouldEnforce(user) && MessageTooLong(user, text))
            {
                clearReason = m_lengthOptions.Message;
            }

            if (clearReason != null)
                ClearChat(bot, user, clearReason);
            else if (!user.IsModerator && !user.IsStreamer)
                m_lastMsgs[m_currMsg++ % m_lastMsgs.Length] = new Tuple<TwitchUser, string>(user, text.ToLower());
        }

        private bool CheckAndTimeoutSpam(WinterBot bot, TwitchUser user, string text)
        {
            if (user.IsSubscriber)
                return false;

            if (String.IsNullOrWhiteSpace(m_clearMsg))
                return false;

            if (text.ToLower().Contains(m_clearMsg))
            {
                if (m_spamTimeout == null)
                    bot.Ban(user);
                else
                    bot.Timeout(user, (int)m_spamTimeout);

                return true;
            }

            return false;
        }

        private bool IsFakeSubscribe(string text)
        {
            text = text.Trim();
            string submsg = "just subscribed";
            
            if (text.Length != submsg.Length && text.Length != submsg.Length + 1)
                return false;

            return text.StartsWith(submsg, StringComparison.CurrentCultureIgnoreCase);
        }

        private bool MessageTooLong(TwitchUser user, string text)
        {
            int max = m_lengthOptions.GetMaxLength(user);
            if (max <= 0)
                return false;

            return text.Length > max;
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
            if (!m_chatOptions.ShouldTimeout(user))
                timeout.Count = 1;

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
            int max = m_emoteOptions.GetMax(user);

            if (m_defaultImageSet != null)
            {
                foreach (string item in m_defaultImageSet)
                {
                    count += CountEmote(message, item);
                    if (count > max)
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
                        if (count > max)
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

        private bool TooManyCaps(TwitchUser user, string message)
        {
            int minLength = m_capsOptions.GetMinLength(user);
            int capsPercent = m_capsOptions.GetPercent(user);
            if (minLength <= 0 || capsPercent <= 0)
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
            
            if (minLength > 0 && total < minLength)
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

            return c == '♥' || c == '…' || c == '€' || (m_symbolOptions.AllowKorean && IsKoreanCharacter(c));
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

            TwitchEmoticonResponse emotes = null;
            try
            {
                emotes = JsonConvert.DeserializeObject<TwitchEmoticonResponse>(data);
            }
            catch (Exception e)
            {
                m_winterBot.WriteDiagnostic(DiagnosticFacility.Error, "Error reading emoticon response: " + e.ToString());
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

            m_defaultImageSet = defaultSet;
            m_imageSets = imageSets;
        }
    }
}