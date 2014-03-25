using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter
{
    public class Options : IniReader
    {
        string m_stream, m_twitchName, m_oauthPass;
        string m_dataDirectory;

        bool m_autoMessage, m_timeoutUrls, m_timeoutEmotes, m_timeoutCaps, m_timeoutSpecialChars, m_userCommands;
        bool m_allowKorean;
        bool m_saveLog, m_saveBinaryLog, m_regulars;
        private int m_maxCaps;
        private int m_maxCapsPercent;
        private string m_capsMessage;
        private int m_emoteMax;
        private string m_emoteMessage;
        string m_specialCharMesssage;
        string m_urlTimeoutMessage, m_urlBanMessage;
        string[] m_whitelist, m_blacklist, m_banlist;
        bool m_passive;

        bool m_timeoutLongMessages;
        string m_longTimeoutMessage;
        int m_maxMessageLength;

        public string Channel { get { return m_stream; } }
        public string Username { get { return m_twitchName; } }
        public string Password { get { return m_oauthPass; } }
        public string DataDirectory { get { return m_dataDirectory; } }
        public bool AutoMessage { get { return m_autoMessage; } }
        public bool TimeoutUrls { get { return m_timeoutUrls; } }
        public bool TimeoutEmotes { get { return m_timeoutEmotes; } }
        public bool TimeoutCaps { get { return m_timeoutCaps; } }
        public bool TimeoutSpecialChars { get { return m_timeoutSpecialChars; } }
        public bool TimeoutLongMessages { get { return m_timeoutLongMessages; } }
        public bool AllowKorean { get { return m_allowKorean; } }
        public bool UserCommands { get { return m_userCommands; } }
        public bool SaveLog { get { return m_saveLog; } }
        public bool SaveBinaryLog { get { return m_saveBinaryLog; } }
        public bool Regulars { get { return m_regulars;  } }
        public bool Timeouts { get { return m_timeoutCaps || m_timeoutEmotes || m_timeoutSpecialChars || m_timeoutUrls || m_timeoutLongMessages; } }

        public int MaxCaps { get { return m_maxCaps; } }
        public int MaxCapsPercent { get { return m_maxCapsPercent; } }
        public string CapsTimeoutMesssage { get { return m_capsMessage ?? "Please don't spam caps."; } }

        public int EmoteMax { get { return m_emoteMax; } }
        public string EmoteMessage { get { return m_emoteMessage ?? "Please don't spam emotes."; } }

        public string SpecialCharMessage { get { return m_specialCharMesssage ?? "Sorry, no special characters allowed."; } }
        public string UrlTimeoutMessage { get { return m_urlTimeoutMessage ?? "Sorry, links are not allowed."; } }
        public string UrlBanMessage { get { return m_urlBanMessage ?? "Banned."; } }

        public string LongMessageTimeout { get { return m_longTimeoutMessage ?? "Sorry, your message was too long."; } }
        public int MaxMessageLength { get { return m_maxMessageLength; } }

        public string[] UrlWhitelist { get { return m_whitelist ?? new string[0]; } }
        public string[] UrlBlacklist { get { return m_blacklist ?? new string[0]; } }
        public string[] UrlBanlist { get { return m_banlist; } }
        public bool Passive { get { return m_passive; } }

        

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
            m_timeoutCaps = true;
            m_timeoutEmotes = true;
            m_timeoutSpecialChars = true;
            m_timeoutUrls = true;
            m_timeoutLongMessages = true;
            m_allowKorean = true;
            m_saveLog = true;
            m_saveBinaryLog = false;
            m_userCommands = true;
            m_regulars = true;

            section = GetSectionByName("features");
            if (section != null)
            {
                section.GetValue("automessage", ref m_autoMessage);
                section.GetValue("timeoutcapsspam", ref m_timeoutCaps);
                section.GetValue("timeoutemotespam", ref m_timeoutEmotes);
                section.GetValue("timeoutlongmessages", ref m_timeoutLongMessages);
                section.GetValue("timeouturls", ref m_timeoutUrls);
                section.GetValue("timeoutsymbols", ref m_timeoutSpecialChars);
                section.GetValue("savelog", ref m_saveLog);
                section.GetValue("savebinarylog", ref m_saveBinaryLog);
                section.GetValue("usercommands", ref m_userCommands);
                section.GetValue("regulars", ref m_regulars);
            }

            m_maxCaps = 16;
            m_maxCapsPercent = 70;

            section = GetSectionByName("capstimeout");
            if (section != null)
            {
                section.GetValue("maxcaps", ref m_maxCaps);
                section.GetValue("maxcapspercent", ref m_maxCapsPercent);
                section.GetValue("message", ref m_capsMessage);
            }

            m_emoteMax = 3;
            m_emoteMessage = "";

            section = GetSectionByName("emotetimeout");
            if (section != null)
            {
                section.GetValue("maxemotes", ref m_emoteMax);
                section.GetValue("message", ref m_emoteMessage);
            }

            section = GetSectionByName("symboltimeout");
            if (section != null)
            {
                section.GetValue("allowkorean", ref m_allowKorean);
                section.GetValue("message", ref m_specialCharMesssage);
            }

            section = GetSectionByName("urltimeout");
            if (section != null)
            {
                section.GetValue("message", ref m_urlTimeoutMessage);
                section.GetValue("banmessage", ref m_urlBanMessage);
            }

            m_timeoutLongMessages = true;
            m_maxMessageLength = 300;

            section = GetSectionByName("messagetimeout");
            if (section != null)
            {
                section.GetValue("message", ref m_longTimeoutMessage);
                section.GetValue("maxmessagelength", ref m_maxMessageLength);
            }

            section = GetSectionByName("whitelist");
            if (section != null)
                m_whitelist = (from r in section.EnumerateRawStrings() where !string.IsNullOrWhiteSpace(r) select r).ToArray();

            section = GetSectionByName("blacklist");
            if (section != null)
                m_blacklist = (from r in section.EnumerateRawStrings() where !string.IsNullOrWhiteSpace(r) select r).ToArray();

            section = GetSectionByName("banlist");
            if (section != null)
                m_banlist = (from r in section.EnumerateRawStrings() where !string.IsNullOrWhiteSpace(r) select r).ToArray();
        }
    }
}
