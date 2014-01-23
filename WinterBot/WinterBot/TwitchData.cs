using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WinterBot
{
    [Serializable]
    public class TwitchData
    {
        List<TwitchUser> m_users;
        [NonSerialized]
        Dictionary<string, TwitchUser> m_userMap;

        public List<TwitchChatEvent> Messages { get; set; }

        public List<TwitchTimeoutEvent> Timeouts { get; set; }

        public HashSet<string> Regulars { get; set; }

        public IEnumerable<TwitchUser> Users { get { return m_users; } }

        public List<string> Log { get; private set; }

        string m_channel;

        public TwitchData()
        {
            Messages = new List<TwitchChatEvent>();
            Timeouts = new List<TwitchTimeoutEvent>();
            Log = new List<string>();
            Regulars = new HashSet<string>();
            m_users = new List<TwitchUser>();
        }

        public TwitchData(string channel)
        {
            m_channel = channel;
            Messages = new List<TwitchChatEvent>();
            Timeouts = new List<TwitchTimeoutEvent>();
            Log = new List<string>();
            Regulars = new HashSet<string>();
            m_users = new List<TwitchUser>();
            LoadRegularList();
            LoadModerators();
        }

        public TwitchUser GetUser(string username)
        {
            InitUserMap();
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


        private TwitchUser AddUser(string username)
        {
            int id = m_users.Count;
            TwitchUser user = new TwitchUser(username, id);
            m_users.Add(user);
            m_userMap[username] = user;
            return user;
        }

        public TwitchChatEvent AddChatMessage(DateTime timestamp, TwitchUser user, string text)
        {
            TwitchChatEvent chat = new TwitchChatEvent(timestamp, user, text);
            Messages.Add(chat);
            return chat;
        }

        public TwitchTimeoutEvent AddTimeout(DateTime timestamp, TwitchUser user)
        {
            string[] messages = (from m in Messages
                            where m.User == user.Id
                            orderby m.Timestamp descending
                            select m.Message).Take(3).ToArray();

            TwitchTimeoutEvent timeout = new TwitchTimeoutEvent(timestamp, user, messages);
            Timeouts.Add(timeout);
            return timeout;
        }



        public void Save(string channel, string filename)
        {
            if (File.Exists(filename))
            {
                string backup = filename + ".bak";
                if (File.Exists(backup))
                    File.Delete(backup);

                File.Move(filename, backup);
            }

            using (FileStream stream = File.Create(filename))
            {
                using (GZipStream gzStream = new GZipStream(stream, CompressionLevel.Optimal))
                {
                    BinaryFormatter fmt = new BinaryFormatter();
                    fmt.Serialize(gzStream, this);
                }
            }

            using (FileStream stream = File.Create(channel + "_users.dat"))
            {
                using (GZipStream gzStream = new GZipStream(stream, CompressionLevel.Optimal))
                {
                    BinaryFormatter fmt = new BinaryFormatter();
                    fmt.Serialize(gzStream, m_users);
                    fmt.Serialize(gzStream, Regulars);
                }
            }
        }

        public static TwitchData Load(string filename)
        {
            using (FileStream stream = File.OpenRead(filename))
            {
                using (GZipStream gzStream = new GZipStream(stream, CompressionMode.Decompress))
                {
                    BinaryFormatter fmt = new BinaryFormatter();
                    return (TwitchData)fmt.Deserialize(gzStream);
                }
            }
        }


        public void Clear()
        {
            Messages.Clear();
            Timeouts.Clear();
            Log.Clear();
        }


        void InitUserMap()
        {
            if (m_userMap == null)
            {
                m_userMap = new Dictionary<string, TwitchUser>();
                foreach (var user in m_users)
                    m_userMap[user.Name] = user;
            }
        }

        public void Merge(TwitchData toMerge)
        {
            InitUserMap();
            Dictionary<int, int> map = new Dictionary<int,int>();
            foreach (var user in toMerge.Users)
            {
                TwitchUser localUser;
                if (m_userMap.TryGetValue(user.Name, out localUser))
                {
                    if (user.Id != localUser.Id)
                        map[user.Id] = localUser.Id;
                }
                else
                {
                    localUser = AddUser(user.Name);
                    map[user.Id] = localUser.Id;
                    localUser.IsSubscriber = user.IsSubscriber;
                    localUser.IsModerator = user.IsModerator;
                }
            }

            foreach (var msg in toMerge.Messages)
            {
                int id;
                if (!map.TryGetValue(msg.User, out id))
                    id = msg.User;

                AddChatMessage(msg.Timestamp, m_users[id], msg.Message);
            }

            foreach (var timeout in toMerge.Timeouts)
            {

                int id;
                if (map.TryGetValue(timeout.User, out id))
                    timeout.User = id;

                Timeouts.Add(timeout);
            }

            Messages.Sort((a,b)=>a.Timestamp.CompareTo(b.Timestamp));
            Timeouts.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));

            Console.WriteLine("Merged {0} messages, {1} timeouts, and {2} users.", toMerge.Messages.Count, toMerge.Timeouts.Count, map.Count);
        }

        public bool IsValidUserName(string user)
        {
            return !user.Contains(' ');
        }

        internal void LogTimeout(MessageEvent evt, TwitchUser user, string offense, ModResult result)
        {
            string msg = string.Format("{3}: Timed out user {0} for {1} '{2}'", user.Name, result, offense, evt.Timestamp);
            Log.Add(msg);
            Console.WriteLine(msg);
        }

        internal void LogCommand(MessageEvent evt, TwitchUser user, string cmd, string value, bool success)
        {
            string msg = string.Format("{0}: {1} {2} command {3} '{4}'", evt.Timestamp, user.Name, success ? "executed" : "failed to exeucte", cmd, value);
            Log.Add(msg);
            Console.WriteLine(msg);
        }

        internal void SetRegular(TwitchUser user, bool reg)
        {
            user.IsRegular = reg;
            Regulars.Add(user.Name);
            SaveRegularList();
        }

        private void SaveRegularList()
        {
            string filename = m_channel + "_regulars.dat";
            if (File.Exists(filename))
            {
                string backup = filename + ".bak";
                if (File.Exists(backup))
                    File.Delete(backup);

                File.Move(filename, backup);
            }

            using (FileStream stream = File.Create(filename))
            {
                using (GZipStream gzStream = new GZipStream(stream, CompressionLevel.Optimal))
                {
                    BinaryFormatter fmt = new BinaryFormatter();
                    fmt.Serialize(gzStream, Regulars);
                }
            }

            Console.WriteLine("Saved regular list.");
        }

        public void LoadRegularList()
        {
            string filename = m_channel + "_regulars.dat";
            if (!File.Exists(filename))
                return;

            using (FileStream stream = File.OpenRead(filename))
            {
                using (GZipStream gzStream = new GZipStream(stream, CompressionMode.Decompress))
                {
                    BinaryFormatter fmt = new BinaryFormatter();
                    Regulars = (HashSet<string>)fmt.Deserialize(gzStream);
                }
            }

            var users = from u in Users
                        where Regulars.Contains(u.Name) && !u.IsRegular
                        select u;

            foreach (var user in users)
                user.IsRegular = true;

            Console.WriteLine("Loaded regular list.");
        }

        internal void AddModerator(TwitchUser user)
        {
            if (user.IsModerator)
                return;

            user.IsModerator = true;
            SaveModerators();
        }

        private void SaveModerators()
        {
            string filename = m_channel + "_moderators.dat";
            if (File.Exists(filename))
            {
                string backup = filename + ".bak";
                if (File.Exists(backup))
                    File.Delete(backup);

                File.Move(filename, backup);
            }

            List<string> mods = new List<string>(from u in Users
                                                 where u.IsModerator
                                                 select u.Name);

            using (FileStream stream = File.Create(filename))
            {
                using (GZipStream gzStream = new GZipStream(stream, CompressionLevel.Optimal))
                {
                    BinaryFormatter fmt = new BinaryFormatter();
                    fmt.Serialize(gzStream, mods);
                }
            }

            Console.WriteLine("Saved moderator list.");
        }
        public void LoadModerators()
        {
            string filename = m_channel + "_moderators.dat";
            if (!File.Exists(filename))
                return;

            List<string> mods = null;
            using (FileStream stream = File.OpenRead(filename))
            {
                using (GZipStream gzStream = new GZipStream(stream, CompressionMode.Decompress))
                {
                    BinaryFormatter fmt = new BinaryFormatter();
                    mods = (List<string>)fmt.Deserialize(gzStream);
                }
            }

            foreach (var mod in mods)
                GetUser(mod).IsModerator = true;

            Console.WriteLine("Loaded moderator list.");
        }
    }

    [Serializable]
    public class TwitchUser
    {
        [NonSerialized]
        int m_permitted;

        public string Name { get; set; }
        
        public int Id { get; set; }

        public bool IsModerator { get; set; }

        public bool IsSubscriber { get; set; }

        public TwitchUser()
        {
        }

        public TwitchUser(string name, int id)
        {
            Name = name;
            Id = id;
        }

        public override string ToString()
        {
            return Name;
        }

        public bool IsRegular { get; set; }

        public bool IsAllowedUrl(string offense)
        {
            if (m_permitted > 0)
            {
                m_permitted--;
                return true;
            }

            return IsModerator || IsRegular || IsSubscriber;
        }

        public void PermitLinkPost(int count)
        {
            m_permitted = count;
        }

        internal bool AllowedOffense(ModResult result)
        {
            return IsModerator;
        }

        public bool IsTurbo { get; set; }
    }

    [Serializable]
    public class TwitchChatEvent
    {
        public int User { get; set; }

        public DateTime Timestamp { get; set; }

        public string Message { get; set; }

        public TwitchChatEvent(DateTime timestamp, TwitchUser user, string text)
        {
            User = user.Id;
            Message = text;
            Timestamp = timestamp;
        }
    }

    [Serializable]
    public class TwitchTimeoutEvent
    {
        public int User { get; set; }
        public DateTime Timestamp { get; set; }

        public string[] LastMessages { get; set; }

        public TwitchTimeoutEvent(DateTime timestamp, TwitchUser user, string[] messages)
        {
            User = user.Id;
            Timestamp = timestamp;
            LastMessages = messages;
        }
    }
}
