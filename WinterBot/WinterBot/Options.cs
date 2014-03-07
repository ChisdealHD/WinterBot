using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinterBot
{
    public class Options
    {
        public IniReader RawIniData;
        string m_stream, m_twitchName, m_oauthPass;

        public string Channel { get { return m_stream; } }
        public string Username { get { return m_twitchName; } }
        public string Password { get { return m_oauthPass; } }

        public Options(string filename)
        {
            RawIniData = new IniReader(filename);
            LoadData();
        }

        private void LoadData()
        {
            var reader = RawIniData;
            IniSection section = reader.GetSectionByName("stream");
            if (section == null)
                throw new InvalidOperationException("Options file missing [Stream] section.");
            
            GetStringValue(section, out m_stream, "stream", section.GetValue("stream"));
            GetStringValue(section, out m_twitchName, "twitchname", section.GetValue("twitchname") ?? section.GetValue("user") ?? section.GetValue("username"));
            GetStringValue(section, out m_oauthPass, "oauth", section.GetValue("oauth") ?? section.GetValue("pass") ?? section.GetValue("password"));
        }

        private void GetStringValue(IniSection section, out string key, string name, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new FormatException(string.Format("Section [{0}] is missing value '{1}'.", section.Name, name));

            key = value;
        }
    }
}
