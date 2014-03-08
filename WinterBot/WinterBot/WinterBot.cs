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
    public delegate void WinterBotCommand(WinterBot sender, TwitchUser user, string cmd, string value);

    public enum DiagnosticLevel
    {
        Diagnostic,
        Notify,
        Warning,
        Error
    }

    public class WinterBot
    {
        private TwitchClient m_twitch;
        ConcurrentQueue<Tuple<Delegate, object[]>> m_events = new ConcurrentQueue<Tuple<Delegate, object[]>>();
        AutoResetEvent m_event = new AutoResetEvent(false);
        string m_channel;

        Dictionary<string, CmdValue> m_commands = new Dictionary<string, CmdValue>();
        private Options m_options;
        HashSet<string> m_regulars = new HashSet<string>();

        #region Events
        /// <summary>
        /// Fired when a user subscribes to the channel.
        /// </summary>
        public event UserEventHandler UserSubscribed;

        /// <summary>
        /// Fired when a chat message is received.
        /// </summary>
        public event MessageHandler MessageReceived;

        /// <summary>
        /// Fired when a user is timed out or banned (we don't know for how long).
        /// </summary>
        public event UserEventHandler UserTimedOut;

        /// <summary>
        /// Fired occasionally to let addons do periodic work.
        /// </summary>
        public event TickHandler Tick;

        /// <summary>
        /// Called when a !command is run, but no handler is registered.
        /// </summary>
        public event UnknownCommandHandler UnknownCommandReceived;

        /// <summary>
        /// Event handler for when messages are received from the chat channel.
        /// </summary>
        /// <param name="msg">The message received.</param>
        public delegate void MessageHandler(WinterBot sender, TwitchUser user, string text);

        /// <summary>
        /// Event handler for when user-related events occur.
        /// </summary>
        /// <param name="user">The user in question.</param>
        public delegate void UserEventHandler(WinterBot sender, TwitchUser user);

        /// <summary>
        /// Event handler called occasionally by the main processing loop of the bot.
        /// </summary>
        /// <param name="sender">The instance of WinterBot.</param>
        public delegate void TickHandler(WinterBot sender, TimeSpan timeSinceLastUpdate);

        /// <summary>
        /// Event called when a user runs a !command, but no handler is available.
        /// </summary>
        /// <param name="user"></param>
        /// <param name="cmd">The command used, with the !.</param>
        /// <param name="value"></param>
        public delegate void UnknownCommandHandler(WinterBot sender, TwitchUser user, string cmd, string value);
        #endregion

        public Options Options { get { return m_options; } }

        public TwitchData UserData
        {
            get
            {
                return m_twitch.ChannelData;
            }
        }

        public WinterBot(Options options, string channel, string user, string oauth)
        {
            m_options = options;
            m_channel = channel.ToLower();

            MessageReceived += TryProcessCommand;

            m_twitch = new TwitchClient();
            m_twitch.InformChatClear += ClearChatHandler;
            m_twitch.MessageReceived += ChatMessageReceived;
            m_twitch.UserSubscribed += SubscribeHandler;

            LoadRegulars();
            LoadExtensions();
        }

        private void LoadExtensions()
        {
            AddCommands(new BuiltinCommands(this));
            AddCommands(new TimeoutController(this));
            AddCommands(new AutoMessage(this));
            AddCommands(new UserCommands(this));
        }

        private void AddCommands(object commands)
        {
            var methods = from m in commands.GetType().GetMethods()
                          where m.IsPublic
                          let attrs = m.GetCustomAttributes(typeof(BotCommandAttribute), false)
                          where attrs.Length == 1
                          select new
                          {
                              Attribute = (BotCommandAttribute)attrs[0],
                              Method = (WinterBotCommand)Delegate.CreateDelegate(typeof(WinterBotCommand), commands, m)
                          };

            foreach (var method in methods)
                foreach (string cmd in method.Attribute.Commands)
                    AddCommand(cmd, method.Method, method.Attribute.AccessRequired);
        }

        public void AddCommand(string cmd, WinterBotCommand command, AccessLevel requiredAccess)
        {
            m_commands[cmd] = new CmdValue(command, requiredAccess);
        }

        public void WriteDiagnostic(DiagnosticLevel level, string msg)
        {
            Console.WriteLine(msg);
        }

        public void WriteDiagnostic(DiagnosticLevel level, string msg, params object[] values)
        {
            WriteDiagnostic(level, string.Format(msg, values));
        }

        public void SendMessage(string msg)
        {
            m_twitch.SendMessage(msg);
        }

        public void SendMessage(string fmt, params object[] param)
        {
            SendMessage(string.Format(fmt, param));
        }


        #region Event Wrappers
        private void OnUnknownCommand(TwitchUser user, string cmd, string value)
        {
            var evt = UnknownCommandReceived;
            if (evt != null)
                evt(this, user, cmd, value);
        }


        private void OnTick(TimeSpan timeSpan)
        {
            var evt = Tick;
            if (evt != null)
                evt(this, timeSpan);
        }
        #endregion

        #region Giant Switch Statement
        private void ChatMessageReceived(TwitchClient source, TwitchUser user, string text)
        {
            var evt = MessageReceived;
            if (evt != null)
            {
                m_events.Enqueue(new Tuple<Delegate, object[]>(evt, new object[] { this, user, text }));
                m_event.Set();
            }
        }

        private void ClearChatHandler(TwitchClient source, TwitchUser user)
        {
            var evt = UserTimedOut;
            if (evt != null)
            {
                m_events.Enqueue(new Tuple<Delegate, object[]>(evt, new object[] { this, user }));
                m_event.Set();
            }
        }

        private void SubscribeHandler(TwitchClient source, TwitchUser user)
        {
            var evt = UserSubscribed;
            if (evt != null)
            {
                m_events.Enqueue(new Tuple<Delegate, object[]>(evt, new object[] { this, user }));
                m_event.Set();
            }
        }

        public void Go()
        {
            if (!Connect())
                return;

            Stopwatch timer = new Stopwatch();
            timer.Start();

            while (true)
            {
                m_event.WaitOne(250);

                Tuple<Delegate, object[]> evt;
                while (m_events.TryDequeue(out evt))
                {
                    Delegate function = evt.Item1;
                    object[] args = evt.Item2;

                    function.DynamicInvoke(args);
                }

                if (timer.Elapsed.TotalSeconds >= 15)
                {
                    timer.Stop();
                    OnTick(timer.Elapsed);
                    timer.Restart();
                }
            }
        }
        #endregion

        #region Helpers

        private void TryProcessCommand(WinterBot sender, TwitchUser user, string text)
        {
            Debug.Assert(sender == this);

            string cmd, value;
            if (!TryReadCommand(text, out cmd, out value))
                return;

            Debug.Assert(cmd != null);
            Debug.Assert(value != null);

            CmdValue command;
            if (m_commands.TryGetValue(cmd, out command))
            {
                if (!CanUseCommand(user, command.Access))
                    return;

                command.Command(this, user, cmd, value);
            }
            else
            {
                OnUnknownCommand(user, cmd, value);
            }
        }

        public bool CanUseCommand(TwitchUser user, AccessLevel required)
        {
            bool isStreamer = m_channel.Equals(user.Name, StringComparison.CurrentCultureIgnoreCase);
            switch (required)
            {
                case AccessLevel.Normal:
                    return true;

                case AccessLevel.Mod:
                    return isStreamer || user.IsModerator;

                case AccessLevel.Subscriber:
                    return isStreamer || user.IsSubscriber || user.IsModerator;

                case AccessLevel.Regular:
                    return isStreamer || user.IsSubscriber || user.IsModerator || m_regulars.Contains(user.Name.ToLower());

                case AccessLevel.Streamer:
                    return isStreamer;

                default:
                    return false;
            }
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

            cmd = text.Substring(start, end - start);
            if (end < text.Length)
                value = text.Substring(end + 1).Trim();
            else
                value = "";

            return true;
        }
        private bool Connect()
        {
            bool connected = m_twitch.Connect(m_channel, m_options.Username, m_options.Password);

            if (connected)
                Console.WriteLine("Connected to {0}...", m_channel);
            else
                Console.WriteLine("Failed to connect!");
            return connected;
        }
        #endregion

        struct CmdValue
        {
            public AccessLevel Access;
            public WinterBotCommand Command;

            public CmdValue(WinterBotCommand command, AccessLevel accessRequired)
            {
                Access = accessRequired;
                Command = command;
            }
        }

        internal void AddRegular(string value)
        {
            if (!m_regulars.Contains(value))
            {
                m_regulars.Add(value);
                SaveRegulars();
            }
        }

        internal void RemoveRegular(string value)
        {
            if (m_regulars.Contains(value))
            {
                m_regulars.Remove(value);
                SaveRegulars();
            }
        }

        void LoadRegulars()
        {
            string regulars = GetRegularFile();
            if (File.Exists(regulars))
                m_regulars = new HashSet<string>(File.ReadAllLines(regulars));
            else
                m_regulars = new HashSet<string>();
        }

        void SaveRegulars()
        {
            var regulars = GetRegularFile();
            if (m_regulars.Count > 0)
                File.WriteAllLines(regulars, m_regulars);
            else if (File.Exists(regulars))
                File.Delete(regulars);
        }

        private string GetRegularFile()
        {
            return Path.Combine(Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), m_channel + "_regulars.txt");
        }

        internal bool IsRegular(TwitchUser user)
        {
            return m_regulars.Contains(user.Name.ToLower());
        }
    }

    public enum AccessLevel
    {
        Normal,
        Regular,
        Subscriber,
        Mod,
        Streamer
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class BotCommandAttribute : Attribute
    {
        public string[] Commands { get; set; }
        public AccessLevel AccessRequired { get; set; }

        public BotCommandAttribute(AccessLevel accessRequired, params string[] commands)
        {
            Commands = commands;
            AccessRequired = accessRequired;
        }
    }

}
