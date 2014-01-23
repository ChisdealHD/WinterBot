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
    public enum AccessLevel
    {
        Normal,
        Regular,
        Subscriber,
        Mod,
        Streamer
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class BotCommandAttribute : Attribute
    {
        public string[] Commands { get; set; }
        public AccessLevel AccessRequired { get; set; }

        public BotCommandAttribute(AccessLevel accessRequired, params string[] commands)
        {
            Commands = commands;
            AccessRequired = accessRequired;
        }
    }

    public class BuiltInCommands
    {
        private WinterBot m_winterBot;

        public BuiltInCommands(WinterBot bot)
        {
            m_winterBot = bot;
        }

        [BotCommand(AccessLevel.Mod, "addreg", "addregular")]
        public void AddRegular(TwitchUser user, string cmd, string value)
        {
            SetRegular(cmd, value, true);
        }

        [BotCommand(AccessLevel.Mod, "delreg", "delregular", "remreg", "remregular")]
        public void RemoveRegular(TwitchUser user, string cmd, string value)
        {
            SetRegular(cmd, value, false);
        }

        private void SetRegular(string cmd, string value, bool regular)
        {
            value = value.Trim().ToLower();

            var userData = m_winterBot.UserData;
            if (!userData.IsValidUserName(value))
            {
                m_winterBot.WriteDiagnostic(DiagnosticLevel.Notify, "{0}: Invalid username '{1}.", cmd, value);
                return;
            }

            var reg = userData.GetUser(value);
            reg.IsRegular = regular;

            if (regular)
                m_winterBot.SendMessage("{0} added to regular list.", value);
            else
                m_winterBot.SendMessage("{0} removed from regular list.", value);
        }

    }

    public class TimeoutController
    {
        private WinterBot m_winterBot;
        HashSet<string> m_permit = new HashSet<string>();
        HashSet<string> m_allowedUrls = new HashSet<string>();
        HashSet<string> m_urlExtensions = new HashSet<string>();
        Regex m_url = new Regex(@"([\w-]+\.)+([\w-]+)(/[\w- ./?%&=]*)?", RegexOptions.IgnoreCase);

        public TimeoutController(WinterBot bot)
        {
            LoadExtensions();
            m_winterBot = bot;
            m_winterBot.MessageReceived += CheckMessage;
        }

        [BotCommand(AccessLevel.Mod, "permit")]
        public void Permit(TwitchUser user, string cmd, string value)
        {
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
            if (user.IsRegular || user.IsSubscriber || user.IsModerator)
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

            string url;
            if (HasUrl(text, out url))
            {
                text = text.ToLower();
                url = url.ToLower();
                if (!m_allowedUrls.Contains(url) || (url.Contains("teamliquid") && (text.Contains("userfiles") || text.Contains("image") || text.Contains("profile"))))
                {
                    if (url.Contains("naked-julia.com") || url.Contains("codes4free.net") || url.Contains("slutty-kate.com"))
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
            if (total <= 15)
                return false;


            int percent = 100 * upper / total;
            if (percent < 70)
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

        void LoadExtensions()
        {
            var exts = File.ReadAllLines(@"extensions.txt");
            m_urlExtensions = new HashSet<string>(exts);

            var allowed = File.ReadAllLines(@"whitelist_urls.txt");
            m_allowedUrls = new HashSet<string>(allowed);
        }

    }


    // TODO: User text commands
    // TODO: Interval message commands
}