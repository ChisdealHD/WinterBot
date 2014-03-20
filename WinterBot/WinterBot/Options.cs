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

        public string Channel { get { return m_stream; } }
        public string Username { get { return m_twitchName; } }
        public string Password { get { return m_oauthPass; } }
        public string Data { get { return m_dataDirectory; } }

        public bool AutoMessage { get { return m_autoMessage; } }
        public bool TimeoutUrls { get { return m_timeoutUrls; } }
        public bool TimeoutEmotes { get { return m_timeoutEmotes; } }
        public bool TimeoutCaps { get { return m_timeoutCaps; } }
        public bool TimeoutSpecialChars { get { return m_timeoutSpecialChars; } }
        public bool AllowKorean { get { return m_allowKorean; } }
        public bool UserCommands { get { return m_userCommands; } }

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
            
            GetStringValue(section, out m_stream, "stream", section.GetValue("stream"));
            GetStringValue(section, out m_twitchName, "twitchname", section.GetValue("twitchname") ?? section.GetValue("user") ?? section.GetValue("username"));
            GetStringValue(section, out m_oauthPass, "oauth", section.GetValue("oauth") ?? section.GetValue("pass") ?? section.GetValue("password"));
            section.GetValue("DataDirectory", ref m_dataDirectory);

            // Set defaults
            var messages = GetSectionByName("messages");
            m_autoMessage = messages != null && messages.EnumerateRawStrings().FirstOrDefault() != null;
            m_timeoutCaps = true;
            m_timeoutEmotes = true;
            m_timeoutSpecialChars = true;
            m_timeoutUrls = true;
            m_allowKorean = true;

            section = GetSectionByName("features");
            if (section != null)
            {
                section.GetValue("automessage", ref m_autoMessage);
                section.GetValue("timeoutcaps", ref m_timeoutCaps);
                section.GetValue("timeoutemotes", ref m_timeoutEmotes);
                section.GetValue("timeouturls", ref m_timeoutUrls);
            }

            section = GetSectionByName("chat");
            if (section != null)
                section.GetValue("allowkorean", ref m_allowKorean);
        }

        private void GetStringValue(IniSection section, out string key, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException(string.Format("Section [{0}] is missing value '{1}'.", section.Name, name));

            key = value;
        }
    }
}
