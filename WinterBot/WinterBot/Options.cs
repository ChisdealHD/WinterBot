using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter
{
    class Option<T>
    {
        T m_all, m_reg, m_sub;

        public Option(T allDefault)
        {
            m_all = allDefault;
            m_reg = allDefault;
            m_sub = allDefault;
        }

        public Option(T allDefault, T regDefault, T subDefault)
        {
            m_all = allDefault;
            m_reg = regDefault;
            m_sub = subDefault;
        }


        public T GetValue(TwitchUser user)
        {
            if (user.IsSubscriber)
                return m_sub;

            if (user.IsRegular)
                return m_reg;

            return m_all;
        }

        public void Init(GetValueFunc getValue, string name)
        {
            Init(getValue, name, "Regular" + name, "Subscriber" + name);
        }

        public void Init(GetValueFunc getValue, string name, string regName, string subName)
        {
            getValue(name, ref m_all);
            getValue(regName, ref m_reg);
            getValue(subName, ref m_sub);
        }

        public delegate bool GetValueFunc(string name, ref T val);
    }

    public abstract class FeatureOptions
    {
        bool m_enabled = true;
        bool m_enforceReg, m_enforceSub;

        public bool Enabled { get { return m_enabled; } set { m_enabled = value; } }
        public bool EnforceRegular { get { return m_enforceReg; } set { m_enforceReg = value; } }
        public bool EnforceSub { get { return m_enforceSub; } set { m_enforceSub = value; } }

        public bool ShouldEnforce(TwitchUser user)
        {
            if (!Enabled || user.IsModerator)
                return false;

            if (user.IsSubscriber)
                return EnforceSub;

            if (user.IsRegular)
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
        string m_message = "Sorry, links are not allowed.";
        string m_banMessage = "Banned.";

        public string[] Whitelist { get { return m_whitelist; } }
        public string[] Blacklist { get { return m_blacklist; } }
        public string[] Banlist { get { return m_banlist; } }

        public string Message { get { return m_message ?? ""; } }

        public string BanMessage { get { return m_banMessage ?? ""; } }


        public UrlTimeoutOptions(Options options)
        {
            m_whitelist = m_blacklist = m_banlist = new string[0];

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
                section.GetValue("Message", ref m_message);
                section.GetValue("BanMessage", ref m_banMessage);
            }
        }
    }

    public class CapsTimeoutOptions : FeatureOptions
    {
        Option<int> m_length = new Option<int>(16);
        Option<int> m_percent = new Option<int>(70);
        string m_message = "Please don't spam caps.";

        public int GetMinLength(TwitchUser user)
        {
            return m_length.GetValue(user);
        }

        public int GetPercent(TwitchUser user)
        {
            return m_percent.GetValue(user);
        }

        public string Message { get { return m_message; } }


        public CapsTimeoutOptions(Options options)
        {
            var section = options.GetSectionByName("capstimeout");
            base.Init(section);
            if (section != null)
            {
                m_length.Init(section.GetValue, "MaxCaps");
                m_percent.Init(section.GetValue, "MaxCapsPercent");
                section.GetValue("Message", ref m_message);
            }
        }
    }

    public class LengthTimeoutOptions : FeatureOptions
    {
        string m_message = "Sorry, your message was too long.";
        Option<int> m_maxLength = new Option<int>(300);

        public string Message { get { return m_message; } }

        public int GetMaxLength(TwitchUser user)
        {
            return m_maxLength.GetValue(user);
        }

        public LengthTimeoutOptions(Options options)
        {
            var section = options.GetSectionByName("LongMessageTimeout");
            base.Init(section);
            if (section != null)
            {
                m_maxLength.Init(section.GetValue, "MaxLength");
                section.GetValue("Message", ref m_message);
            }
        }
    }

    public class SymbolTimeoutOptions : FeatureOptions
    {
        string m_message = "Sorry, no special characters allowed.";
        bool m_allowKorean = true;

        public bool AllowKorean { get { return m_allowKorean; } }
        public string Message { get { return m_message; } }

        public SymbolTimeoutOptions(Options options)
        {
            var section = options.GetSectionByName("symboltimeout");
            base.Init(section);
            if (section != null)
            {
                section.GetValue("AllowKorean", ref m_allowKorean);
                section.GetValue("Message", ref m_message);
            }
        }
    }

    public class EmoteTimeoutOptions : FeatureOptions
    {
        string m_message = "Please don't spam emotes.";
        Option<int> m_max = new Option<int>(3, 5, 10);

        public string Message { get { return m_message; } }

        public int GetMax(TwitchUser user)
        {
            return m_max.GetValue(user);
        }

        public EmoteTimeoutOptions(Options options)
        {
            var section = options.GetSectionByName("emotetimeout");
            base.Init(section);
            if (section != null)
            {
                m_max.Init(section.GetValue, "MaxEmotes");
                section.GetValue("Message", ref m_message);
            }
        }
    }

    public class ChatOptions
    {
        string m_subMessage = null;
        string m_followMessage = null;
        bool m_saveLog = true;
        bool m_saveBinaryLog = false;
        bool m_userCommands = true;

        Option<bool> m_neverTimeout = new Option<bool>(false, false, true);

        public bool ShouldTimeout(TwitchUser user)
        {
            return !m_neverTimeout.GetValue(user);
        }

        public string SubscribeMessage { get { return m_subMessage; } }
        public string FollowMessage { get { return m_followMessage; } }

        public bool SaveLog { get { return m_saveLog; } }
        public bool SaveBinaryLog { get { return m_saveBinaryLog; } }

        public bool UserCommandsEnabled { get { return m_userCommands; } }

        public ChatOptions(Options options)
        {
            var messages = options.GetSectionByName("messages");

            var chat = options.GetSectionByName("chat");
            if (chat != null)
            {
                chat.GetValue("SubscribeMessage", ref m_subMessage);
                chat.GetValue("FollowMessage", ref m_followMessage);
                chat.GetValue("SaveLog", ref m_saveLog);
                chat.GetValue("SaveBinaryLog", ref m_saveBinaryLog);
                chat.GetValue("UserCommands", ref m_userCommands);
                m_neverTimeout.Init(chat.GetValue, "NeverTimeout", "NeverTimeoutRegulars", "NeverTimeoutSubscribers");
            }
        }
    }

    public class AutoMessageOptions
    {
        bool m_enabled, m_random;
        string[] m_messages;
        int m_delay = 5;
        int m_messageDelay = 25;

        public bool Enabled { get { return m_enabled; } set { m_enabled = value; } }
        public bool Random { get { return m_random; } }
        public string[] Messages { get { return m_messages; } }
        public int Delay { get { return m_delay >= 10 ? m_delay : 10; } }
        public int MessageDelay { get { return m_messageDelay; } }

        public AutoMessageOptions(Options options)
        {
            var messageSection = options.GetSectionByName("messages");
            var settings = options.GetSectionByName("automessage");
            if (messageSection == null || settings == null)
                return;

            var messages = from s in messageSection.EnumerateRawStrings()
                           where !string.IsNullOrWhiteSpace(s)
                           select s.Trim();

            m_messages = messages.ToArray();

            if (m_messages.Length == 0)
                return;

            if (settings != null)
            {
                settings.GetValue("Enabled", ref m_enabled);
                settings.GetValue("Delay", ref m_delay);
                settings.GetValue("Random", ref m_random);
                settings.GetValue("MessageDelay", ref m_messageDelay);
            }
        }
    }

    public class Options : IniReader
    {
        string m_stream, m_twitchName, m_oauthPass;
        string m_dataDirectory;

        bool m_regulars;
        bool m_passive;

        ChatOptions m_chatOptions;
        UrlTimeoutOptions m_urlOptions;
        CapsTimeoutOptions m_capsOptions;
        LengthTimeoutOptions m_lengthOptions;
        SymbolTimeoutOptions m_symbolOptions;
        EmoteTimeoutOptions m_emoteOptions;
        AutoMessageOptions m_autoMessageOptions;

        public string Channel { get { return m_stream; } }
        public string Username { get { return m_twitchName; } }
        public string Password { get { return m_oauthPass; } }
        public string DataDirectory { get { return m_dataDirectory; } }
        public bool Regulars { get { return m_regulars; } }


        public bool Passive { get { return m_passive; } }

        public ChatOptions ChatOptions { get { return m_chatOptions; } }
        public UrlTimeoutOptions UrlOptions { get { return m_urlOptions; } }
        public CapsTimeoutOptions CapsOptions { get { return m_capsOptions; } }
        public LengthTimeoutOptions LengthOptions { get { return m_lengthOptions; } }
        public SymbolTimeoutOptions SymbolOptions { get { return m_symbolOptions; } }
        public EmoteTimeoutOptions EmoteOptions { get { return m_emoteOptions; } }
        public AutoMessageOptions AutoMessageOptions { get { return m_autoMessageOptions; } }

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

            m_regulars = true;

            section = GetSectionByName("Regulars");
            if (section != null)
            {
                section.GetValue("Enabled", ref m_regulars);
            }

            m_urlOptions = new UrlTimeoutOptions(this);
            m_capsOptions = new CapsTimeoutOptions(this);
            m_lengthOptions = new LengthTimeoutOptions(this);
            m_symbolOptions = new SymbolTimeoutOptions(this);
            m_emoteOptions = new EmoteTimeoutOptions(this);
            m_chatOptions = new ChatOptions(this);
            m_autoMessageOptions = new AutoMessageOptions(this);
        }
    }
}
