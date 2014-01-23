using System;
using System.Collections.Concurrent;
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
    class WinterBot
    {
        private TwitchClient m_twitch;
        ConcurrentQueue<BaseEvent> m_events = new ConcurrentQueue<BaseEvent>();
        AutoResetEvent m_event = new AutoResetEvent(false);
        TwitchData m_data;
        string m_channel;
        string m_filename;
        WinterBotController m_controller;

        public WinterBot(string channel)
        {
            m_channel = channel.ToLower();
            m_controller = new WinterBotController(m_channel);
            m_filename = m_channel.ToLower();
            m_data = new TwitchData(channel);

            InitTwitchClient();
        }

        private void InitTwitchClient()
        {
            if (m_twitch != null)
            {
                m_twitch.InformChatClear -= ClearChatHandler;
                m_twitch.InformModerator -= InformModeratorHandler;
                m_twitch.InformSubscriber -= InformSubscriberHandler;
                m_twitch.MessageReceived -= MessageHandler;
                m_twitch.UserSubscribed -= SubscribeHandler;
            }

            m_twitch = new TwitchClient();
            m_twitch.InformChatClear += ClearChatHandler;
            m_twitch.InformModerator += InformModeratorHandler;
            m_twitch.InformSubscriber += InformSubscriberHandler;
            m_twitch.MessageReceived += MessageHandler;
            m_twitch.UserSubscribed += SubscribeHandler;
        }

        private void OnSaveData()
        {
            m_data.Save(m_channel, m_filename);
        }

        private void OnUserTimedOut(TimeoutEvent evt, TwitchUser user)
        {
            m_data.AddTimeout(evt.Timestamp, user);
        }

        private void OnUserSubscribed(UserSubscribeEvent userSubscribeEvent, TwitchUser user)
        {
            user.IsSubscriber = true;
        }

        private void OnNewChatMessage(MessageEvent evt, TwitchUser user)
        {
            m_data.AddChatMessage(evt.Timestamp, user, evt.Text);

            if (WasMessageRejected(evt, user))
                return;

            TryProcessCommand(evt, user);
        }

        private bool TryProcessCommand(MessageEvent evt, TwitchUser user)
        {
            string cmd, value;
            if (!m_controller.TryReadCommand(evt.Text, out cmd, out value))
                return false;

            Debug.Assert(cmd != null);
            Debug.Assert(value != null);

            WinterBotCommand command = m_controller.GetCommand(cmd);
            if (command == null)
                return false;


            bool success = command.CanUseCommand(user) &&
                           command.Execute(user, cmd, value, m_data, m_twitch, m_controller);


            m_data.LogCommand(evt, user, cmd, value, success);
            return success;
        }

        private bool WasMessageRejected(MessageEvent evt, TwitchUser user)
        {
            if (user.IsModerator || user.IsSubscriber || user.IsRegular)
                return false;

            var text = evt.Text;

            string offense;
            var result = m_controller.CheckMessage(text, out offense);
            if (result == ModResult.DenyUrl && user.IsAllowedUrl(offense))
                return false;
    
            if (result != ModResult.Allow)
            {
                if (user.AllowedOffense(result))
                    return false;

                OnRejectMessage(evt, user, text, offense, result);
                return true;
            }

            return false;
        }

        private void OnRejectMessage(MessageEvent evt, TwitchUser user, string text, string offense, ModResult result)
        {
            m_data.LogTimeout(evt, user, offense, result);

            int msec = (DateTime.Now - evt.Timestamp).Milliseconds;

            const int delay = 300;
            if (msec < delay)
                Thread.Sleep(delay - msec);

            if (result == ModResult.BanUrl)
            {
                m_twitch.SendMessage(string.Format("{0}: Banned.", user.Name));
                m_twitch.Ban(user.Name);
            }
            else
            {
                if (result == ModResult.DenyUrl)
                    m_twitch.SendMessage(string.Format("{0}: Only subscribers are allowed to post links. (This is not a timeout.)", user.Name));
                else if (result == ModResult.DenySymbols)
                    m_twitch.SendMessage(string.Format("{0}: Sorry, no special characters allowed to keep the dongers to a minimum. (This is not a timeout.)", user.Name));
                else if (result == ModResult.DenyCaps)
                    m_twitch.SendMessage(string.Format("{0}: Sorry, please don't spam caps. (This is not a timeout.)", user.Name));

                m_twitch.Timeout(user.Name, 1);
            }
        }

        private void OnSetSubscriberStatus(SubscriberNotifyEvent subscribeEvent, TwitchUser user)
        {
            user.IsSubscriber = true;
        }

        private void OnSetModeratorStatus(ModeratorNotifyEvent moderatorEvent, TwitchUser user)
        {
            m_data.AddModerator(user);
        }

        private void OnTick()
        {
            bool live = m_twitch.IsStreamLive;
            if (live)
            {
                string msg = m_controller.NextBotMessage();
                if (!string.IsNullOrEmpty(msg))
                    m_twitch.SendMessage(msg);
            }
        }

        #region Giant Switch Statement
        public void Go()
        {
            if (!Connect())
                return;

            bool needSave = false;
            Stopwatch saver = new Stopwatch();
            saver.Start();

            Stopwatch ticker = new Stopwatch();
            ticker.Start();

            Stopwatch lastMessage = new Stopwatch();
            lastMessage.Start();

            int messages = 0, total = 0;

            while (true)
            {
                m_event.WaitOne(250);

                BaseEvent evt;
                while (m_events.TryDequeue(out evt))
                {
                    total++;

                    var timestamp = evt.Timestamp;
                    TwitchUser user = evt.User;
                    switch (evt.Kind)
                    {
                        case EventKind.InformModerator:
                            OnSetModeratorStatus((ModeratorNotifyEvent)evt, user);
                            break;

                        case EventKind.InformSubscriber:
                            OnSetSubscriberStatus((SubscriberNotifyEvent)evt, user);
                            break;

                        case EventKind.Subscribe:
                            OnUserSubscribed((UserSubscribeEvent)evt, user);
                            break;

                        case EventKind.Message:
                            messages++;
                            needSave = true;
                            OnNewChatMessage((MessageEvent)evt, user);
                            break;

                        case EventKind.Timeout:
                            needSave = true;
                            OnUserTimedOut((TimeoutEvent)evt, user);
                            break;

                        default:
                            Debug.Assert(false, "Unknown event type.");
                            break;
                    }
                }

                if (ticker.Elapsed.Minutes >= 5)
                {
                    OnTick();
                    ticker.Restart();
                }

                if (saver.Elapsed.Minutes >= 1)
                {
                    if (needSave)
                        OnSaveData();

                    needSave = false;
                    saver.Restart();

                    if (total > 0)
                    {
                        Console.WriteLine("{0}: Processed {1} messages, {2} total events.", DateTime.Now, messages, total);

                        messages = 0;
                        total = 0;
                        lastMessage.Restart();
                    }
                }

                if (m_twitch.IsStreamLive && lastMessage.Elapsed.Minutes >= 5)
                {
                    bool first = true;
                    do
                    {
                        if (!first)
                            Thread.Sleep(10000);

                        first = false;
                        InitTwitchClient();
                    } while (!Connect());
                }
            }
        }

        private bool Connect()
        {
            bool connected = m_twitch.Connect(m_channel, "", "");
            if (connected)
                Console.WriteLine("Connected to {0}...", m_channel);
            else
                Console.WriteLine("Failed to connect!");
            return connected;
        }
        #endregion

        #region Async Event Handlers
        private void MessageHandler(TwitchClient source, TwitchUser user, string text)
        {
            m_events.Enqueue(new MessageEvent(user, text));
            m_event.Set();
        }

        private void InformSubscriberHandler(TwitchClient source, TwitchUser user)
        {
            m_events.Enqueue(new SubscriberNotifyEvent(user));
            m_event.Set();
        }

        private void InformModeratorHandler(TwitchClient source, TwitchUser user)
        {
            m_events.Enqueue(new ModeratorNotifyEvent(user));
            m_event.Set();
        }

        private void ClearChatHandler(TwitchClient source, TwitchUser user)
        {
            m_events.Enqueue(new TimeoutEvent(user));
            m_event.Set();
        }

        private void SubscribeHandler(TwitchClient source, TwitchUser user)
        {
            m_events.Enqueue(new UserSubscribeEvent(user));
            m_event.Set();
        }
        #endregion
    }


    public enum ModResult
    {
        Allow,
        DenyUrl,
        DenySymbols,
        Command,
        DenyCaps,
        BanUrl
    }


    public class WinterBotController
    {
        Regex m_url = new Regex(@"([\w-]+\.)+([\w-]+)(/[\w- ./?%&=]*)?", RegexOptions.IgnoreCase);
        private HashSet<string> m_urlExtensions;
        HashSet<string> m_allowedUrls;
        Dictionary<string, WinterBotCommand> m_commands = new Dictionary<string, WinterBotCommand>();
        List<string> m_messages;
        string m_channel;
        Random m_rand = new Random();

        public IEnumerable<WinterBotCommand> Commands
        {
            get
            {
                return m_commands.Values;
            }
        }

        public WinterBotController(string channel)
        {
            m_channel = channel;
            LoadExtensions();
            AddCommand(new UserCommandController());
            AddCommand(new PermitCommand());
            AddCommand(new RegularCommand());
            LoadCommandList();
            LoadMessageList();
        }

        public ModResult CheckMessage(string message, out string offender)
        {
            offender = null;
            if (HasSpecialCharacter(message))
            {
                offender = message;
                return ModResult.DenySymbols;
            }

            if (TooManyCaps(message, out offender))
                return ModResult.DenyCaps;

            string url;
            if (HasUrl(message, out url))
            {
                message = message.ToLower();
                url = url.ToLower();
                if (!m_allowedUrls.Contains(url) || (url.Contains("teamliquid") && (message.Contains("userfiles") || message.Contains("image") || message.Contains("profile"))))
                {
                    offender = url;

                    if (url.Contains("naked-julia.com") || url.Contains("codes4free.net") || url.Contains("slutty-kate.com"))
                        return ModResult.BanUrl;

                    return ModResult.DenyUrl;
                }
            }

            return ModResult.Allow;
        }

        private bool TooManyCaps(string message, out string offender)
        {
            int upper = 0;
            int lower = 0;

            foreach (char c in message)
            {
                if ('a' <= c && c <= 'z')
                    lower++;
                else if ('A' <= c && c <= 'Z')
                    upper++;
            }

            offender = null;
            int total = lower + upper;
            if (total <= 15)
                return false;


            int percent = 100 * upper / total;
            if (percent < 70)
                return false;

            offender = message;
            return true;
        }


        static bool HasSpecialCharacter(string str)
        {
            for (int i = 0; i < str.Length; ++i)
                if (!Allowed(str[i]))
                    return true;

            return false;
        }

        static bool Allowed(char c)
        {
            if (c < 255)
                return true;

            // punctuation block
            if (0x2010 <= c && c <= 0x2049)
                return true;

            return c == '♥' || c == '…' || c == '€' || IsKoreanCharacter(c);
        }

        static bool IsKoreanCharacter(char c)
        {
            return (0xac00 <= c && c <= 0xd7af) ||
                (0x1100 <= c && c <= 0x11ff) ||
                (0x3130 <= c && c <= 0x318f) ||
                (0x3200 <= c && c <= 0x32ff) ||
                (0xa960 <= c && c <= 0xa97f) ||
                (0xd7b0 <= c && c <= 0xd7ff) ||
                (0xff00 <= c && c <= 0xffef);
        }

        bool HasUrl(string str, out string url)
        {
            url = null;
            var match = m_url.Match(str);
            if (!match.Success)
                return false;

            var groups = match.Groups;
            if (!m_urlExtensions.Contains(groups[groups.Count - 2].Value))
                return false;

            url = groups[1].Value + groups[2].Value;
            return true;
        }

        void LoadExtensions()
        {
            var exts = File.ReadAllLines(@"extensions.txt");
            m_urlExtensions = new HashSet<string>(exts);

            var allowed = File.ReadAllLines(@"whitelist_urls.txt");
            m_allowedUrls = new HashSet<string>(allowed);
        }

        internal bool TryReadCommand(string text, out string cmd, out string value)
        {
            cmd = null;
            value = null;

            int i = text.IndexOf('!');
            if (i == -1)
                return false;

            int start = ++i;
            if (start >= text.Length)
                return false;

            int end = text.IndexOf(' ', start);

            if (end == -1)
                end = text.Length;

            if (start == end)
                return false;

            cmd = text.Substring(start, end-start);
            if (end < text.Length)
                value = text.Substring(end + 1).Trim();
            else
                value = "";

            return true;
        }

        public WinterBotCommand GetCommand(string cmd)
        {
            WinterBotCommand result;
            m_commands.TryGetValue(cmd, out result);

            return result;
        }

        internal void AddCommand(WinterBotCommand command)
        {
            foreach (string cmd in command.Commands)
                m_commands[cmd] = command;

            if (command is UserCommand)
                SaveCommandList();
        }

        internal bool RemoveCommand(string value)
        {
            return m_commands.Remove(value);
        }


        private void SaveCommandList()
        {
            string filename = m_channel + "_commands.dat";
            if (File.Exists(filename))
            {
                string backup = filename + ".bak";
                if (File.Exists(backup))
                    File.Delete(backup);

                File.Move(filename, backup);
            }


            List<UserCommand> userCmds = new List<UserCommand>(from cmd in Commands
                                                               where cmd is UserCommand
                                                               select (UserCommand)cmd);

            using (FileStream stream = File.Create(filename))
            {
                using (GZipStream gzStream = new GZipStream(stream, CompressionLevel.Optimal))
                {
                    BinaryFormatter fmt = new BinaryFormatter();
                    fmt.Serialize(gzStream, userCmds);
                }
            }

            Console.WriteLine("Saved command list.");
        }

        void LoadCommandList()
        {
            string filename = m_channel + "_commands.dat";
            if (!File.Exists(filename))
                return;

            List<UserCommand> cmds = null;
            using (FileStream stream = File.OpenRead(filename))
            {
                using (GZipStream gzStream = new GZipStream(stream, CompressionMode.Decompress))
                {
                    BinaryFormatter fmt = new BinaryFormatter();
                    cmds = (List<UserCommand>)fmt.Deserialize(gzStream);
                }
            }

            foreach (var cmd in cmds)
                foreach (string name in cmd.Commands)
                    m_commands[name] = cmd;

            Console.WriteLine("Loaded command list.");
        }

        void LoadMessageList()
        {
            string filename = m_channel + "_messages.txt";
            if (!File.Exists(filename))
                return;

            string[] values = File.ReadAllLines(filename);
            m_messages = new List<string>(from line in values
                                          let str = line.Trim()
                                          where str.Length > 0 && str[0] != '.' && str[1] != '/'
                                          select str);
        }


        public string NextBotMessage()
        {
            if (m_messages == null || m_messages.Count == 0)
                return null;

            int i = m_rand.Next(0, m_messages.Count - 1);
            return m_messages[i];
        }
    }

}
