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

        public string Channel { get { return m_stream; } }
        public string Username { get { return m_twitchName; } }
        public string Password { get { return m_oauthPass; } }
        public string Data { get { return m_dataDirectory; } }

        public Options(string filename)
            : base(filename)
        {
            m_dataDirectory = Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
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
        }

        private void GetStringValue(IniSection section, out string key, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException(string.Format("Section [{0}] is missing value '{1}'.", section.Name, name));

            key = value;
        }
    }
}
