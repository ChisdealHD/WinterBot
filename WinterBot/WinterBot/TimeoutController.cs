using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;

namespace Winter
{
    public class TimeoutController
    {
        #region Private Variables
        WinterBot m_winterBot;

        HashSet<TwitchUser> m_permit = new HashSet<TwitchUser>();

        UserSet m_denyList;

        List<RegexMatch> m_urlWhitelist;
        List<RegexMatch> m_urlBlacklist;
        List<RegexMatch> m_urlBanlist;
        List<RegexMatch> m_wordBanlist;

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
        BanWordOptions m_banWordOptions;
        #endregion

        #region Initialization
        public TimeoutController(WinterBot bot)
        {
            m_winterBot = bot;
            LoadOptions(bot);

            m_denyList = new UserSet(bot, "deny");

            m_winterBot.MessageReceived += CheckMessage;
            m_winterBot.ActionReceived += CheckAction;
            m_winterBot.StreamOffline += StreamStateChange;
            m_winterBot.StreamOnline += StreamStateChange;
        }


        void LoadOptions(WinterBot bot)
        {
            Options options = bot.Options;
            m_options = options;
            m_chatOptions = options.ChatOptions;
            m_urlOptions = options.UrlOptions;
            m_capsOptions = options.CapsOptions;
            m_lengthOptions = options.LengthOptions;
            m_symbolOptions = options.SymbolOptions;
            m_emoteOptions = options.EmoteOptions;
            m_banWordOptions = options.BanWordOptions;

            // Load url lists
            m_urlWhitelist = new List<RegexMatch>(m_urlOptions.Whitelist.Select(s => new UrlMatch(bot, s)));
            m_urlBlacklist = new List<RegexMatch>(m_urlOptions.Blacklist.Select(s => new UrlMatch(bot, s)));
            m_urlBanlist = new List<RegexMatch>(m_urlOptions.Banlist.Select(s => new UrlMatch(bot, s)));
            m_wordBanlist = new List<RegexMatch>(m_banWordOptions.BanList.Select(s => new WordMatch(bot, s)));
        }
        #endregion

        #region Link Commands
        [BotCommand(AccessLevel.Mod, "deny")]
        public void Deny(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            value = value.Trim();

            if (!TwitchUsers.IsValidUserName(value))
            {
                sender.SendResponse(Importance.Med, "{0}: Usage: !deny [user]", user.Name);
                return;
            }

            var target = sender.Users.GetUser(value);
            if (target.IsModerator)
                return;

            if (m_permit.Contains(target))
                m_permit.Remove(target);

            m_denyList.Add(target);
            sender.SendResponse(Importance.High, "{0}: {1} is no longer allowed to post links.", user.Name, target.Name);
        }

        [BotCommand(AccessLevel.Mod, "permit", "allow")]
        public void Permit(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            Debug.Assert(m_winterBot == sender);

            value = value.Trim();
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
                if (m_urlOptions.ShouldEnforce(target))
                    m_winterBot.SendResponse(Importance.Med, "{0}: {1} was removed from the deny list.", user.Name, target.Name);
                else
                    m_winterBot.SendResponse(Importance.Med, "{0}: {1} can now post links again.", user.Name, target.Name);
            }
            else
            {
                if (m_urlOptions.ShouldEnforce(target))
                {
                    m_permit.Add(target);
                    m_winterBot.SendResponse(Importance.Med, "{0} -> {1} has been granted permission to post a single link.", user.Name, target.Name);
                }
                else
                {
                    m_winterBot.SendResponse(Importance.Low, "{0}: {1} can posts links.", user.Name, target.Name);
                }
            }
        }
        #endregion

        #region Feature Enable/Disable
        [BotCommand(AccessLevel.Mod, "capsmode")]
        public void CapsMode(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            m_capsOptions.Enabled = ChangeMode(sender, user, value, "Caps", m_capsOptions.Enabled);
        }

        [BotCommand(AccessLevel.Mod, "linkmode")]
        public void LinkMode(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            m_urlOptions.Enabled = ChangeMode(sender, user, value, "Link", m_urlOptions.Enabled);
        }

        [BotCommand(AccessLevel.Mod, "symbolmode")]
        public void SymbolMode(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            m_symbolOptions.Enabled = ChangeMode(sender, user, value, "Symbol", m_symbolOptions.Enabled);
        }

        [BotCommand(AccessLevel.Mod, "longmessagemode")]
        public void LongMessageMode(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            m_lengthOptions.Enabled = ChangeMode(sender, user, value, "Long message", m_lengthOptions.Enabled);
        }


        [BotCommand(AccessLevel.Mod, "emotemode")]
        public void EmoteMode(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            m_emoteOptions.Enabled = ChangeMode(sender, user, value, "Emote", m_emoteOptions.Enabled);
        }


        private static bool ChangeMode(WinterBot sender, TwitchUser user, string value, string type, bool curr)
        {
            bool result = curr;

            value = value.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                sender.SendResponse(Importance.Med, "{0} protect is currently {1}.", type, curr ? "enabled" : "disabled");
            }
            else if (value.ParseBool(ref result))
            {
                if (curr != result)
                {
                    string enableStr = result ? "enabled" : "disabled";
                    sender.SendResponse(Importance.Med, "{0} protect is now {1}.", type, enableStr);
                    sender.WriteDiagnostic(DiagnosticFacility.ModeChange, "{0} changed {1} mode to {2}.", user.Name, type, enableStr);
                }
            }
            return result;
        }
        #endregion

        #region Spam Control
        [BotCommand(AccessLevel.Mod, "banurl")]
        public void Banliust(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            UrlMatch match = new UrlMatch(sender, value);
            m_urlBanlist.Add(match);
            sender.SendResponse(Importance.Med, "Added {0} to the url ban list.", value);
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
                        sender.SendResponse(Importance.High, "Usage: !spam [-time=??] text to auto timeout");
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
                    sender.SendResponse(Importance.High, "Banning all messages containing '{0}'.", value);
                else if (timeout <= 1)
                    sender.SendResponse(Importance.High, "All messages containing '{0}' will be purged.", value);
                else
                    sender.SendResponse(Importance.High, "All messages containing '{0}' will receive a {1} second timeout.", value, timeout);
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
        #endregion

        #region Chat Monitoring
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
            RegexMatch banWord = null;

            Url[] urls;
            if (HasUrls(text, out urls))
            {
                // Check bans.
                if (MatchesAny(urls, m_urlBanlist))
                {
                    m_winterBot.Ban(user);
                    if (!string.IsNullOrEmpty(m_urlOptions.BanMessage))
                        bot.SendUnconditional("{0}: {1}", user.Name, m_urlOptions.BanMessage);

                    m_winterBot.WriteDiagnostic(DiagnosticFacility.Ban, "Banned {0} for {1}.", user.Name, string.Join(", ", urls.Select(url => url.FullUrl)));
                }
                else if ((m_urlOptions.ShouldEnforce(user) || m_denyList.Contains(user)) && (!MatchesAll(urls, m_urlWhitelist) || MatchesAny(urls, m_urlBlacklist)))
                {
                    if (m_permit.Contains(user))
                        m_permit.Remove(user);
                    else
                        clearReason = m_urlOptions.Message;
                }
            }
            else if (m_banWordOptions.ShouldEnforce(user) && HasBannedWord(text, out banWord))
            {
                clearReason = EnforceBannedWord(bot, user, banWord.String);
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

        private string EnforceBannedWord(WinterBot bot, TwitchUser user, string word)
        {
            int timeout = m_banWordOptions.TimeOut;
            string msg = m_banWordOptions.Message;

            if (!string.IsNullOrWhiteSpace(msg))
                msg = msg.Replace("$word", word);

            if (timeout <= 0)
                return msg;
            
            if (!m_chatOptions.ShouldTimeout(user))
                timeout = 1;

            ClearChat(bot, user, null, timeout);
            if (m_timeouts[user].Count == 1)
                m_timeouts[user].Count++;

            if (!string.IsNullOrWhiteSpace(msg))
            {
                if (timeout == 1)
                    bot.Send(MessageType.Timeout, Importance.Med, "{0}: {1} (This is not a timeout.)", user.Name, msg);
                else
                    bot.Send(MessageType.Timeout, Importance.Med, "{0}: {1} ({2} second timeout.)", user.Name, msg, timeout);
            }

            return null;
        }

        bool HasBannedWord(string text, out RegexMatch banWord)
        {
            banWord = null;
            foreach (var word in m_wordBanlist)
            {
                if (word.IsMatch(text))
                {
                    banWord = word;
                    return true;
                }
            }

            return false;
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

        private static bool MatchesAny(Url[] urls, List<RegexMatch> regexes)
        {
            return urls.Any(url => regexes.Any(regex => regex.IsMatch(url.FullUrl)));
        }

        private static bool MatchesAll(Url[] urls, List<RegexMatch> regexes)
        {
            return urls.All(url => regexes.Any(regex => regex.IsMatch(url.FullUrl)));
        }

        private bool TooManyEmotes(TwitchUser user, string message)
        {
            int max = m_emoteOptions.GetMax(user);

            var imgSet = TwitchHttp.Instance.ImageSet;
            if (imgSet == null)
                return false;

            return imgSet.TooManySymbols(message, max, user.IconSet);
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

            return c == '™' || c == '♥' || c == '…' || c == '€' || (m_symbolOptions.AllowKorean && IsKoreanCharacter(c));
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

        bool HasUrls(string str, out Url[] urls)
        {
            urls = str.FindUrls();
            return urls != null && urls.Length > 0;
        }
        #endregion

        #region Helpers
        private void StreamStateChange(WinterBot sender)
        {
            // Clean up some memory, no need to keep old timeouts around.
            if (m_timeouts.Count == 0)
                return;

            var items = from item in m_timeouts
                        where GetEffectiveCount(item.Value) > 0
                        select item;

            m_timeouts = items.ToDictionary(t=>t.Key, t=>t.Value);
        }

        int GetEffectiveCount(TimeoutCount timeout)
        {
            int curr = timeout.Count;
            int diff = (int)(DateTime.Now - timeout.LastTimeout).TotalMinutes / 15;

            if (diff > 0)
                curr -= diff;

            if (curr < 0)
                curr = 0;

            return curr;
        }


        private void ClearChat(WinterBot sender, TwitchUser user, string clearReason, int minTimeout=1)
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
                timeout.Count = GetEffectiveCount(timeout) + 1;
            }

            timeout.LastTimeout = now;
            if (!m_chatOptions.ShouldTimeout(user))
                timeout.Count = 1;

            if (minTimeout < 1)
                minTimeout = 1;

            int duration = 0;
            switch (timeout.Count)
            {
                case 1:
                case 2:
                    duration = 1;
                    break;

                case 3:
                    duration = 5 * 60;
                    break;

                case 4:
                    duration = 10 * 60;
                    break;

                case 5:
                    duration = 8 * 60 * 60;
                    break;
            }

            if (duration < minTimeout)
                duration = minTimeout;

            if (shouldMessage)
            {
                if (duration == 1)
                    sender.Send(MessageType.Timeout, Importance.Med, "{0}: {1} (This is not a timeout.)", user.Name, clearReason);
                else
                    sender.Send(MessageType.Timeout, Importance.High, "{0}: {1}", user.Name, clearReason);
            }

            sender.Timeout(user, duration);
            timeout.LastTimeout = now.AddSeconds(duration);
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
        #endregion
    }

    abstract class RegexMatch
    {
        protected Regex m_reg;
        protected string m_str;

        public string String { get { return m_str; } }

        public bool IsMatch(string str)
        {
            if (m_reg != null)
                return m_reg.IsMatch(str);

            return str.ToLower().Contains(m_str);
        }
    }

    class WordMatch : RegexMatch
    {

        public WordMatch(WinterBot bot, string str)
        {
            m_str = str.ToLower();
            if (str.IsRegex())
            {
                try
                {
                    m_reg = new Regex(str, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException)
                {
                    bot.WriteDiagnostic(DiagnosticFacility.UserError, "Invalid regex in options: " + str);
                }
            }
        }
    }

    class UrlMatch : RegexMatch
    {
        public UrlMatch(WinterBot bot, string str)
        {
            if (str.StartsWith("http://", StringComparison.CurrentCultureIgnoreCase))
                str = str.Substring(7);

            m_str = str.ToLower();
            if (str.IsRegex())
            {
                try
                {
                    m_reg = new Regex(str, RegexOptions.IgnoreCase);
                }
                catch (ArgumentException)
                {
                    bot.WriteDiagnostic(DiagnosticFacility.UserError, "Invalid regex in options: " + str);
                }
            }
        }
    }
}