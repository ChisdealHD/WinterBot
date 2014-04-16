using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter
{
    public class Args
    {
        public string FullText { get; private set; }
        public string Arguments { get { return (m_argStart == -1 || m_argStart >= FullText.Length) ? string.Empty : FullText.Substring(m_argStart).Trim(); } }

        public string Error { get; private set; }

        public void Reset()
        {
            m_curr = m_argStart;
            Error = null;
        }


        Dictionary<string, string> m_flags;

        int m_curr, m_argStart;
        TwitchUsers m_users;

        internal Args(WinterBot bot, string text)
        {
            m_users = bot.Users;
            FullText = text;
            ParseFlags();
        }

        internal Args(TwitchUsers users, string text)
        {
            m_users = users;
            FullText = text;
            ParseFlags();
        }

        public TwitchUser GetUser()
        {
            MovePastWhitespace();

            string name = GetOneWord(' ');

            if (name == null)
            {
                Error = "Expected twitch user.";
                return null;
            }
            else if (!TwitchUsers.IsValidUserName(name))
            {
                Error = string.Format("{0} is not a valid twitch user.", name);
                return null;
            }

            MovePastWhitespace();

            return m_users.GetUser(name);
        }

        public int GetInt()
        {
            int value;
            GetInt(out value);
            return value;
        }


        public string GetOneWord()
        {
            MovePastWhitespace();

            string strVal = GetOneWord(' ');
            if (strVal == null)
            {
                Error = "Expected value.";
                return null;
            }

            MovePastWhitespace();
            return string.IsNullOrWhiteSpace(strVal) ? null : strVal;
        }

        public string GetString()
        {
            if (IsComplete())
                return string.Empty;

            int curr = m_curr;
            m_curr = FullText.Length;
            return FullText.Substring(curr).Trim();
        }

        public bool GetInt(out int value)
        {
            value = 0;

            MovePastWhitespace();

            string strVal = GetOneWord(' ');
            if (strVal == null)
            {
                Error = "Expected integer.";
                return false;
            }
            else if (!int.TryParse(strVal, out value))
            {
                Error = string.Format("{0} is not an integer.", value);
                return false;
            }
            
            MovePastWhitespace();
            return true;
        }




        public bool GetFlag(string flag)
        {
            return m_flags == null ? false : m_flags.ContainsKey(flag.ToLower());
        }

        public AccessLevel GetAccessFlag(string name, AccessLevel defaultValue = default(AccessLevel), bool required = false)
        {
            AccessLevel level;
            if (GetAccessFlag(name, out level, required))
                return level;

            return defaultValue;
        }

        public bool GetAccessFlag(string name, out AccessLevel level, bool required = false)
        {
            level = AccessLevel.Mod;

            string access;
            if (m_flags == null || !m_flags.TryGetValue(name.ToLower(), out access) || access == null)
            {
                if (required)
                    Error = string.Format("Usage: -{0}=level, where level is user, sub, reg, or mod.", name);

                return false;
            }

            switch (access.ToLower())
            {
                case "normal":
                case "user":
                    level = AccessLevel.Normal;
                    break;

                case "sub":
                case "subscriber":
                    level = AccessLevel.Subscriber;
                    break;

                case "regular":
                case "reg":
                    level = AccessLevel.Regular;
                    break;

                case "mod":
                case "moderator":
                    level = AccessLevel.Mod;
                    break;

                case "stream":
                case "streamer":
                    level = AccessLevel.Streamer;
                    break;

                default:
                    Error = string.Format("Invalid user level {0}.", access);
                    return false;
            }

            return true;
        }

        public int GetIntFlag(string name, int defaultValue = default(int), bool required = false)
        {
            int value;

            if (GetIntFlag(name, out value, required))
                return value;

            return defaultValue;
        }


        public bool GetIntFlag(string name, out int value, bool required = false)
        {
            value = 0;

            string strVal;
            if (m_flags == null || !m_flags.TryGetValue(name.ToLower(), out strVal))
            {
                if (required)
                    Error = string.Format("Usage: -{0}=##, where ## is an integer.", name);

                return false;
            }

            if (strVal == null || !int.TryParse(strVal, out value))
            {
                Error = string.Format("Invalid int parameter '{1}'.  Use -{0}=##.", name, strVal);
                return false;
            }

            return true;
        }



        private void ParseFlags()
        {
            MovePastWhitespace();
            char c = CurrentChar();
            while (c == '-' || c == '/')
            {
                if (IsComplete())
                    break;

                m_curr++;
                AddFlag();
                MovePastWhitespace();

                if (IsComplete())
                    break;

                c = FullText[m_curr];
            }

            m_argStart = m_curr;
        }

        private char CurrentChar()
        {
            if (IsComplete())
                return (char)0;

            return FullText[m_curr];
        }

        private void AddFlag()
        {
            if (IsComplete())
                return;

            string flag = GetOneWord(' ', '=');
            if (flag == null)
                return;

            string value = null;
            if (!IsComplete(1) && FullText[m_curr] == '=')
            {
                m_curr++;
                value = GetOneWord(' ');
            }

            if (m_flags == null)
                m_flags = new Dictionary<string, string>();

            m_flags[flag] = value;
        }

        string GetOneWord(params char[] sep)
        {
            if (IsComplete())
                return null;

            string flag = null;
            int start = m_curr;
            int end = m_curr;

            while (end < FullText.Length && !sep.Contains(FullText[end]))
                end++;

            if (start != end)
                flag = FullText.Slice(start, end);

            m_curr = end;
            return flag;
        }

        private bool IsComplete(int extra = 0)
        {
            return m_curr == -1 || m_curr + extra >= FullText.Length;
        }

        private void MovePastWhitespace()
        {
            if (m_curr == -1)
                return;

            int curr = m_curr;
            while (curr < FullText.Length && FullText[curr] == ' ')
                curr++;

            m_curr = curr;
        }
    }
}
