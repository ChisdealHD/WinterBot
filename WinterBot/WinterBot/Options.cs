using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter
{
    public abstract class FeatureOptions
    {
        bool m_enabled = true;
        bool m_enforceReg, m_enforceSub;

        public bool Enabled { get { return m_enabled; } set { m_enabled = value; } }
        public bool EnforceRegular { get { return m_enforceReg; } set { m_enforceReg = value; } }
        public bool EnforceSub { get { return m_enforceSub; } set { m_enforceSub = value; } }

        public bool ShouldEnforce(WinterBot bot, TwitchUser user)
        {
            if (!Enabled || user.IsModerator)
                return false;

            if (user.IsSubscriber)
                return EnforceSub;

            if (bot.IsRegular(user))
                return EnforceRegular;

            return true;
        }

        protected void Init(IniSection section)
        {
            if (section != null)
            {
                section.GetValue("Enabled", ref m_enabled);
                section.GetValue("EnforceForRegulars", ref m_enforceReg);
                section.GetValue("EnforceForSubscribers", ref m_enforceSub);
            }
        }
    }

    public class UrlTimeoutOptions : FeatureOptions
    {
        string[] m_whitelist, m_blacklist, m_banlist;
        string m_message, m_banMessage;

        public string[] Whitelist { get { return m_whitelist; } }
        public string[] Blacklist { get { return m_blacklist; } }
        public string[] Banlist { get { return m_banlist; } }

        public string Message { get { return m_message ?? ""; } }

        public string BanMessage { get { return m_banMessage ?? ""; } }


        public UrlTimeoutOptions()
        {
            Init();
        }

        public UrlTimeoutOptions(Options options)
        {
            Init();

            var section = options.GetSectionByName("whitelist");
            if (section != null)
                m_whitelist = (from r in section.EnumerateRawStrings() where !string.IsNullOrWhiteSpace(r) select r).ToArray();

            section = options.GetSectionByName("blacklist");
            if (section != null)
                m_blacklist = (from r in section.EnumerateRawStrings() where !string.IsNullOrWhiteSpace(r) select r).ToArray();

            section = options.GetSectionByName("banlist");
            if (section != null)
                m_banlist = (from r in section.EnumerateRawStrings() where !string.IsNullOrWhiteSpace(r) select r).ToArray();

            section = options.GetSectionByName("urltimeout");
            base.Init(section);
            if (section != null)
            {
                section.GetValue("message", ref m_message);
                section.GetValue("banmessage", ref m_banMessage);
            }
        }

        private void Init()
        {
            var list = new string[0];
            m_whitelist = list;
            m_blacklist = list;
            m_banlist = list;
            m_message = "Sorry, links are not allowed.";
            m_banMessage = "Banned.";
        }
    }

    public class CapsTimeoutOptions : FeatureOptions
    {
        int m_length = 16, m_percent = 70;
        string m_message = "Please don't spam caps.";

        public int MinLength { get { return m_length; } }
        public int Percent { get { return m_percent; } }
        public string Message { get { return m_message; } }

        public CapsTimeoutOptions()
        {
        }

        public CapsTimeoutOptions(Options options)
        {
            var section = options.GetSectionByName("capstimeout");
            base.Init(section);
            if (section != null)
            {
                section.GetValue("maxcaps", ref m_length);
                section.GetValue("maxcapspercent", ref m_percent);
                section.GetValue("message", ref m_message);
            }
        }
    }

    public class LengthTimeoutOptions : FeatureOptions
    {
        string m_message = "Sorry, your message was too long.";
        int m_maxLength = 300;

        public string Message { get { return m_message; } }
        public int MaxLength { get { return m_maxLength; } }

        public LengthTimeoutOptions(Options options)
        {
            var section = options.GetSectionByName("messagetimeout");
            base.Init(section);
            if (section != null)
            {
                section.GetValue("message", ref m_message);
                section.GetValue("maxmessagelength", ref m_maxLength);
            }
        }

        public LengthTimeoutOptions()
        {
        }
    }

    public class SymbolTimeoutOptions : FeatureOptions
    {
        string m_message = "Sorry, no special characters allowed.";
        bool m_allowKorean = true;

        public bool AllowKorean { get { return m_allowKorean; } }
        public string Message { get { return m_message; } }

        public SymbolTimeoutOptions()
        {
        }

        public SymbolTimeoutOptions(Options options)
        {
            var section = options.GetSectionByName("symboltimeout");
            base.Init(section);
            if (section != null)
            {
                section.GetValue("allowkorean", ref m_allowKorean);
                section.GetValue("message", ref m_message);
            }
        }
    }

    public class EmoteTimeoutOptions : FeatureOptions
    {
        string m_message = "Please don't spam emotes.";
        int m_max = 3;

        public string Message { get { return m_message; } }
        public int Max { get { return m_max; } }

        public EmoteTimeoutOptions()
        {
        }

        public EmoteTimeoutOptions(Options options)
        {
            var section = options.GetSectionByName("emotetimeout");
            base.Init(section);
            if (section != null)
            {
                section.GetValue("maxemotes", ref m_max);
                section.GetValue("message", ref m_message);
            }
        }
    }

    public class ChatOptions
    {
        string m_subMessage = "Thanks for subscribing!";
        string m_followMessage = null;

        public string SubscribeMessage { get { return m_subMessage; } }
        public string FollowMessage { get { return m_followMessage; } }

        public ChatOptions(Options options)
        {
            var chat = options.GetSectionByName("chat");
            if (chat != null)
            {
                chat.GetValue("SubscribeMessage", ref m_subMessage);
                chat.GetValue("FollowMessage", ref m_followMessage);
            }
        }
    }

    public class Options : IniReader
    {
        string m_stream, m_twitchName, m_oauthPass;
        string m_dataDirectory;

        bool m_autoMessage, m_userCommands;
        bool m_saveLog, m_saveBinaryLog, m_regulars;
        bool m_passive;

        ChatOptions m_chatOptions;
        UrlTimeoutOptions m_urlOptions;
        CapsTimeoutOptions m_capsOptions;
        LengthTimeoutOptions m_lengthOptions;
        SymbolTimeoutOptions m_symbolOptions;
        EmoteTimeoutOptions m_emoteOptions;

        public string Channel { get { return m_stream; } }
        public string Username { get { return m_twitchName; } }
        public string Password { get { return m_oauthPass; } }
        public string DataDirectory { get { return m_dataDirectory; } }
        public bool AutoMessage { get { return m_autoMessage; } }
        public bool UserCommands { get { return m_userCommands; } }
        public bool SaveLog { get { return m_saveLog; } }
        public bool SaveBinaryLog { get { return m_saveBinaryLog; } }
        public bool Regulars { get { return m_regulars; } }


        public bool Passive { get { return m_passive; } }

        public ChatOptions ChatOptions { get { return m_chatOptions; } }
        public UrlTimeoutOptions UrlOptions { get { return m_urlOptions; } }
        public CapsTimeoutOptions CapsOptions { get { return m_capsOptions; } }
        public LengthTimeoutOptions LengthOptions { get { return m_lengthOptions; } }
        public SymbolTimeoutOptions SymbolOptions { get { return m_symbolOptions; } }
        public EmoteTimeoutOptions EmoteOptions { get { return m_emoteOptions; } }

        public Options(string filename)
            : base(filename)
        {
            m_dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinterBot");
            LoadData();

            if (!Directory.Exists(m_dataDirectory))
                Directory.CreateDirectory(m_dataDirectory);
        }

        private void LoadData()
        {
            IniSection section = GetSectionByName("stream");
            if (section == null)
                throw new InvalidOperationException("Options file missing [Stream] section.");

            m_stream = section.GetValue("stream");
            m_twitchName = section.GetValue("twitchname") ?? section.GetValue("user") ?? section.GetValue("username");
            m_oauthPass = section.GetValue("oauth") ?? section.GetValue("pass") ?? section.GetValue("password");
            section.GetValue("DataDirectory", ref m_dataDirectory);
            section.GetValue("passive", ref m_passive);

            // Set defaults
            var messages = GetSectionByName("messages");
            m_autoMessage = messages != null && messages.EnumerateRawStrings().FirstOrDefault() != null;
            m_saveLog = true;
            m_saveBinaryLog = false;
            m_userCommands = true;
            m_regulars = true;

            section = GetSectionByName("features");
            if (section != null)
            {
                section.GetValue("automessage", ref m_autoMessage);
                section.GetValue("savelog", ref m_saveLog);
                section.GetValue("savebinarylog", ref m_saveBinaryLog);
                section.GetValue("usercommands", ref m_userCommands);
                section.GetValue("regulars", ref m_regulars);
            }

            m_urlOptions = new UrlTimeoutOptions(this);
            m_capsOptions = new CapsTimeoutOptions(this);
            m_lengthOptions = new LengthTimeoutOptions(this);
            m_symbolOptions = new SymbolTimeoutOptions(this);
            m_emoteOptions = new EmoteTimeoutOptions(this);
            m_chatOptions = new ChatOptions(this);
        }
    }
}
