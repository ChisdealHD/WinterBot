using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Winter;

namespace TwitchChat
{
    class ChatOptions
    {
        string m_stream, m_user, m_oath;
        string[] m_highlightList;
        HashSet<string> m_ignore = new HashSet<string>();
        IniReader m_iniReader;

        bool m_timeouts = false,
            m_bans = true,
            m_questions = true,
            m_sounds = true;

        public string Stream { get { return m_stream; } }
        public string User { get { return m_user; } }
        public string Pass { get { return m_oath; } }

        public string[] Highlights { get { return m_highlightList; } }

        public bool ConfirmTimeouts { get { return m_timeouts; } }
        public bool ConfirmBans { get { return m_bans; } }
        public bool HighlightQuestions { get { return m_questions; } }
        public bool PlaySounds { get { return m_sounds; } }

        public HashSet<string> Ignore { get { return m_ignore; } }

        public ChatOptions()
        {
            m_iniReader = new IniReader("options.ini");

            IniSection section = m_iniReader.GetSectionByName("stream");
            if (section == null)
                throw new InvalidOperationException("Options file missing [Stream] section.");

            m_stream = section.GetValue("stream");
            m_user = section.GetValue("twitchname") ?? section.GetValue("user") ?? section.GetValue("username");
            m_oath = section.GetValue("oauth") ?? section.GetValue("pass") ?? section.GetValue("password");

            section = m_iniReader.GetSectionByName("highlight");
            List<string> highlights = new List<string>();
            if (section != null)
                foreach (string line in section.EnumerateRawStrings())
                    highlights.Add(DoReplacements(line.ToLower()));

            m_highlightList = highlights.ToArray();

            section = m_iniReader.GetSectionByName("ignore");
            if (section != null)
            
            m_ignore = new HashSet<string>((from s in section.EnumerateRawStrings()
                                            where !string.IsNullOrWhiteSpace(s)
                                            select s.ToLower()));

            section = m_iniReader.GetSectionByName("options");
            if (section != null)
            {
                section.GetValue("ConfirmTimeouts", ref m_timeouts);
                section.GetValue("ConfirmBans", ref m_bans);
                section.GetValue("HighlightQuestions", ref m_questions);
                section.GetValue("PlaySounds", ref m_sounds);
            }
        }

        string DoReplacements(string value)
        {
            int i = value.IndexOf("$stream");
            while (i != -1)
            {
                value = value.Replace("$stream", m_stream);
                i = value.IndexOf("$stream");
            }
            return value;
        }
    }
}
