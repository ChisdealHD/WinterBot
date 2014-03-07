using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinterBot
{
    public class TwitchData
    {
        string m_channel;
        List<TwitchUser> m_users;
        Dictionary<string, TwitchUser> m_userMap;
        TwitchClient m_client;

        public IEnumerable<TwitchUser> Users { get { return m_users; } }

        public IEnumerable<TwitchUser> Moderators
        {
            get
            {
                return from user in m_users where user.IsModerator select user;
            }
        }

        public IEnumerable<TwitchUser> Subscribers
        {
            get
            {
                return from user in m_users where user.IsSubscriber select user;
            }
        }

        public IEnumerable<TwitchUser> Regulars
        {
            get
            {
                return from user in m_users where user.IsRegular select user;
            }
        }

        public TwitchData(TwitchClient client, string channel)
        {
            m_client = client;
            m_channel = channel;
            m_users = new List<TwitchUser>();
            m_userMap = new Dictionary<string, TwitchUser>();
        }

        public TwitchUser GetUser(string username)
        {
            username = username.ToLower();

            TwitchUser user;
            if (!m_userMap.TryGetValue(username, out user))
                user = AddUser(username);

            return user;
        }

        public TwitchUser GetUser(int id)
        {
            return m_users[id];
        }


        public bool IsValidUserName(string user)
        {
            return !user.Contains(' ');
        }

        private TwitchUser AddUser(string username)
        {
            Debug.Assert(!m_userMap.ContainsKey(username));

            TwitchUser user = new TwitchUser(m_client, username, m_users.Count);
            m_users.Add(user);
            m_userMap[username] = user;

            return user;
        }
    }

    public class TwitchUser
    {
        TwitchClient m_client;
        bool m_regular;

        public int[] IconSet { get; set; }

        public string Name { get; set; }
        
        public int Id { get; set; }

        public bool IsModerator { get; set; }

        public bool IsSubscriber { get; set; }

        public bool IsTurbo { get; set; }

        public bool IsRegular
        {
            get
            {
                return m_regular;
            }

            set
            {
                if (m_regular != value)
                {
                    m_regular = value;
                }
            }
        }

        public void Ban()
        {
            m_client.Ban(Name);
        }

        public void ClearChat()
        {
            m_client.Timeout(Name, 1);
        }

        public void Timeout(int duration=600)
        {
            m_client.Timeout(Name, duration);
        }

        #region Constructors
        public TwitchUser(TwitchClient client, string name, int id)
        {
            Debug.Assert(client != null);

            Name = name;
            Id = id;
            m_client = client;
        }
        #endregion

        #region Helpers
        public override string ToString()
        {
            return Name;
        }

        internal void SetTwitchClient(TwitchClient client)
        {
            m_client = client;
        }
        #endregion
    }
}
