using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Winter
{
    public class TwitchUsers
    {
        static Regex s_validUsername = new Regex("[a-zA-Z][a-zA-Z0-9_]*");
        Dictionary<string, TwitchUser> m_users;
        HashSet<TwitchUser> m_moderators;
        object m_sync = new object();
        public WinterBot Bot { get; set; }

        internal HashSet<TwitchUser> ModeratorSet
        {
            get
            {
                if (m_moderators == null)
                    m_moderators = new HashSet<TwitchUser>();

                return m_moderators;
            }

            set
            {
                m_moderators = value;
            }
        }

        public IEnumerable<TwitchUser> Moderators
        {
            get
            {
                return m_moderators;
            }
        }

        public TwitchUsers(WinterBot bot)
        {
            m_users = new Dictionary<string, TwitchUser>();
            Bot = bot;
        }

        public TwitchUser GetUser(string username, bool create=true)
        {
            username = username.ToLower();
            TwitchUser user;

            lock (m_sync)
            {
                if (!m_users.TryGetValue(username, out user) && create)
                {
                    user = new TwitchUser(this, username);
                    m_users[username] = user;
                }
            }

            return user;
        }


        public static bool IsValidUserName(string user)
        {
            return s_validUsername.IsMatch(user);
        }
    }

    public class TwitchUser
    {
        private TwitchUsers m_data;
        public int[] IconSet { get; internal set; }

        public string Name { get; internal set; }

        public bool IsModerator { get; internal set; }

        public bool IsSubscriber { get; internal set; }

        public bool IsTurbo { get; internal set; }

        public bool IsRegular
        {
            get
            {
                return m_data.Bot.IsRegular(Name);
            }
            internal set
            {
                var bot = m_data.Bot;
                if (bot.IsRegular(Name) != value)
                {
                    if (value)
                        bot.AddRegular(Name);
                    else
                        bot.RemoveRegular(Name);
                }
            }
        }

        public TwitchUser(TwitchUsers data, string name)
        {
            Name = name.ToLower();
            m_data = data;
            Debug.Assert(data != null);
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
