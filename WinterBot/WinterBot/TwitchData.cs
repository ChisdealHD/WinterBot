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

namespace WinterBot
{
    public class TwitchData
    {
        static Regex s_validUsername = new Regex("[a-zA-Z][a-zA-Z0-9_]*");
        string m_channel;
        List<TwitchUser> m_users;
        Dictionary<string, TwitchUser> m_userMap;
        TwitchClient m_client;
        object m_sync = new object();

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

            lock (m_sync)
            {
                if (!m_userMap.TryGetValue(username, out user))
                {
                    user = new TwitchUser(m_client, username, m_users.Count);
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
        TwitchClient m_client;

        public int[] IconSet { get; set; }

        public string Name { get; set; }
        
        public int Id { get; set; }

        public bool IsModerator { get; set; }

        public bool IsSubscriber { get; set; }

        public bool IsTurbo { get; set; }

        public TwitchUser(TwitchClient client, string name, int id)
        {
            Debug.Assert(client != null);

            Name = name;
            Id = id;
            m_client = client;
        }

        public override string ToString()
        {
            return Name;
        }
    }
}
