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

            m_twitch = new TwitchClient();
            m_twitch.InformChatClear += ClearChatHandler;
            m_twitch.MessageReceived += ChatMessageReceived;
            m_twitch.UserSubscribed += SubscribeHandler;

            LoadExtensions();
        }

        private void LoadExtensions()
        {
            AddCommands(new BuiltInCommands());
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

        private void OnNewChatMessage(TwitchUser user, string text)
        {
            var evt = MessageReceived;
            if (evt != null)
                evt(this, user, text);

            TryProcessCommand(user, text);
        }


        private void OnUserTimedOut(TwitchUser user)
        {
            var evt = UserTimedOut;
            if (evt != null)
                evt(this, user);
        }

        private void OnUserSubscribed(TwitchUser user)
        {
            var evt = UserSubscribed;
            if (evt != null)
                evt(this, user);
        }

        private void OnTick(TimeSpan timeSpan)
        {
            var evt = Tick;
            if (evt != null)
                evt(this, timeSpan);
        }
        #endregion

        #region Giant Switch Statement
        public void Go()
        {
            if (!Connect())
                return;

            DateTime lastTick = DateTime.Now;
            OnTick(new TimeSpan(0));

            while (true)
            {
                m_event.WaitOne(250);

                BaseEvent evt;
                while (m_events.TryDequeue(out evt))
                {
                    var timestamp = evt.Timestamp;
                    TwitchUser user = evt.User;
                    switch (evt.Kind)
                    {
                        case EventKind.Subscribe:
                            OnUserSubscribed(user);
                            break;

                        case EventKind.Message:
                            OnNewChatMessage(user, ((MessageEvent)evt).Text);
                            break;

                        case EventKind.Timeout:
                            OnUserTimedOut(user);
                            break;

                        default:
                            break;
                    }
                }

                DateTime now = DateTime.Now;
                TimeSpan diff = now - lastTick;
                if (diff.TotalMilliseconds > 500)
                {
                    OnTick(diff);
                    lastTick = now;
                }
            }
        }
        #endregion

        #region Helpers

        private void TryProcessCommand(TwitchUser user, string text)
        {
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
            switch (required)
            {
                case AccessLevel.Normal:
                    return true;

                case AccessLevel.Mod:
                    return user.IsModerator;

                case AccessLevel.Subscriber:
                    return user.IsSubscriber || user.IsModerator;

                case AccessLevel.Regular:
                    return user.IsSubscriber || user.IsModerator || user.IsRegular;

                case AccessLevel.Streamer:
                    return m_channel.Equals(user.Name, StringComparison.CurrentCultureIgnoreCase);

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

        #region Twitch Client Helpers
        private void ChatMessageReceived(TwitchClient source, TwitchUser user, string text)
        {
            m_events.Enqueue(new MessageEvent(user, text));
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

        private TwitchClient m_twitch;
        ConcurrentQueue<BaseEvent> m_events = new ConcurrentQueue<BaseEvent>();
        AutoResetEvent m_event = new AutoResetEvent(false);
        string m_channel;

        Dictionary<string, CmdValue> m_commands = new Dictionary<string, CmdValue>();
        private Options m_options;
    }
}
