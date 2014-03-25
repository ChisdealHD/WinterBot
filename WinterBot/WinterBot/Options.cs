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

        public string Channel { get { return m_stream; } }
        public string Username { get { return m_twitchName; } }
        public string Password { get { return m_oauthPass; } }
        public string DataDirectory { get { return m_dataDirectory; } }
        public bool AutoMessage { get { return m_autoMessage; } }
        public bool TimeoutUrls { get { return m_timeoutUrls; } }
        public bool TimeoutEmotes { get { return m_timeoutEmotes; } }
        public bool TimeoutCaps { get { return m_timeoutCaps; } }
        public bool TimeoutSpecialChars { get { return m_timeoutSpecialChars; } }
        public bool AllowKorean { get { return m_allowKorean; } }
        public bool UserCommands { get { return m_userCommands; } }
        public bool SaveLog { get { return m_saveLog; } }
        public bool SaveBinaryLog { get { return m_saveBinaryLog; } }
        public bool Regulars { get { return m_regulars;  } }
        public bool Timeouts { get { return m_timeoutCaps || m_timeoutEmotes || m_timeoutSpecialChars || m_timeoutUrls; } }

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

            // Set defaults
            var messages = GetSectionByName("messages");
            m_autoMessage = messages != null && messages.EnumerateRawStrings().FirstOrDefault() != null;
            m_timeoutCaps = true;
            m_timeoutEmotes = true;
            m_timeoutSpecialChars = true;
            m_timeoutUrls = true;
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
                section.GetValue("timeouturls", ref m_timeoutUrls);
                section.GetValue("timeoutsymbols", ref m_timeoutSpecialChars);
                section.GetValue("savelog", ref m_saveLog);
                section.GetValue("savebinarylog", ref m_saveBinaryLog);
                section.GetValue("usercommands", ref m_userCommands);
                section.GetValue("regulars", ref m_regulars);
            }

            section = GetSectionByName("chat");
            if (section != null)
                section.GetValue("allowkorean", ref m_allowKorean);
        }
    }
}
