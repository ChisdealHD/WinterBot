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
        List<TwitchUser> m_users;
        Dictionary<string, TwitchUser> m_userMap;
        HashSet<TwitchUser> m_moderators;
        object m_sync = new object();

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

        public TwitchUsers()
        {
            m_users = new List<TwitchUser>();
            m_userMap = new Dictionary<string, TwitchUser>();
        }

        public TwitchUser GetUser(string username, bool create=true)
        {
            username = username.ToLower();
            TwitchUser user;

            lock (m_sync)
            {
                if (!m_userMap.TryGetValue(username, out user) && create)
                {
                    user = new TwitchUser(username, m_users.Count);
                    m_users.Add(user);
                    m_userMap[username] = user;
                }
            }

            return user;
        }

        public TwitchUser GetUser(int id)
        {
            lock (m_sync)
            {
                return m_users[id];
            }
        }


        public static bool IsValidUserName(string user)
        {
            return s_validUsername.IsMatch(user);
        }
    }

    public class TwitchUser
    {
        public int[] IconSet { get; internal set; }

        public string Name { get; internal set; }
        
        public int Id { get; internal set; }

        public bool IsModerator { get; internal set; }

        public bool IsSubscriber { get; internal set; }

        public bool IsTurbo { get; internal set; }

        public TwitchUser(string name, int id)
        {
            Name = name.ToLower();
            Id = id;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
