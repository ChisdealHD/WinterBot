using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;


namespace Winter
{
    public enum MessageType
    {
        Message,
        Response,
        Timeout
    }

    public delegate void WinterBotCommand(WinterBot sender, TwitchUser user, string cmd, string value);

    public enum DiagnosticFacility
    {
        UserError,
        Ban,
        Twitch
    }

    public class WinterBot : IDisposable
    {
        TwitchClient m_twitch;
        TwitchUsers m_data;
        ConcurrentQueue<Tuple<Delegate, object[]>> m_events = new ConcurrentQueue<Tuple<Delegate, object[]>>();
        AutoResetEvent m_event = new AutoResetEvent(false);
        string m_channel;

        Dictionary<string, CmdValue> m_commands = new Dictionary<string, CmdValue>();
        private Options m_options;
        HashSet<string> m_regulars = new HashSet<string>();

        volatile bool m_checkUpdates = true;

        Thread m_streamLiveThread, m_streamFollowerThread;
        int m_viewers;

        #region Events
        public event DiagnosticEventHandler DiagnosticMessage;

        /// <summary>
        /// Fired when a user gains moderator status.  This happens when the
        /// streamer promotes a user to a moderator, or simply when a moderator
        /// joins the chat.
        /// </summary>
        public event UserEventHandler ModeratorAdded;

        /// <summary>
        /// Fired when a moderator's status has been downgraded to normal user.
        /// </summary>
        public event UserEventHandler ModeratorRemoved;

        /// <summary>
        /// Fired when a user subscribes to the channel.
        /// </summary>
        public event UserEventHandler UserSubscribed;

        /// <summary>
        /// Fired when a user follows the channel.
        /// </summary>
        public event UserEventHandler UserFollowed;

        /// <summary>
        /// Fired when a chat message is received.
        /// </summary>
        public event MessageHandler MessageReceived;

        /// <summary>
        /// Fired when a user is timed out or banned (we don't know for how long).
        /// </summary>
        public event UserEventHandler ChatClear;

        /// <summary>
        /// Fired when the bot times out a user.  There will also be a ChatClear message
        /// when the server tells the client to clear chat after a timeout is issued.
        /// </summary>
        public event UserTimeoutHandler UserTimedOut;

        /// <summary>
        /// Fired when the bot bans a user.  There will also be a ChatClear message
        /// when the server tells the client to clear chat after a timeout is issued.
        /// </summary>
        public event UserEventHandler UserBanned;

        /// <summary>
        /// Fired occasionally to let addons do periodic work.
        /// </summary>
        public event TickHandler Tick;

        /// <summary>
        /// Called when a !command is run, but no handler is registered.
        /// </summary>
        public event UnknownCommandHandler UnknownCommandReceived;

        /// <summary>
        /// Called when the bot has connected to the user's channel.
        /// </summary>
        public event BotEventHandler Connected;

        /// <summary>
        /// Called when the bot begins a clean shutdown (you may not get this event
        /// if the process is rudely killed).  This is followed by EndShutdown.  It
        /// is expected that BeingShutdown signals that a shutdown is about to occur
        /// but not block, and EndShutdown blocks until critical work is complete.
        /// </summary>
        public event BotEventHandler BeginShutdown;

        /// <summary>
        /// Called when the bot is done with a clean shutdown and is about to terminate
        /// the process.  Note that it is still possible to send and respond to twitch
        /// messages until EndShutdown has completed.
        /// </summary>
        public event BotEventHandler EndShutdown;

        /// <summary>
        /// Fired when the stream goes online.
        /// </summary>
        public event BotEventHandler StreamOnline;

        /// <summary>
        /// Fired when the stream goes offline.
        /// </summary>
        public event BotEventHandler StreamOffline;

        /// <summary>
        /// Fired when the viewer count changes.
        /// </summary>
        public event ViewerEventHandler ViewerCountChanged;

        /// <summary>
        /// Called when a global event for the bot occurs.
        /// </summary>
        /// <param name="sender">The winterbot instance sending the event.</param>
        public delegate void BotEventHandler(WinterBot sender);

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
        /// Event handler for when users are timed out by the bot.
        /// </summary>
        /// <param name="user">The user in question.</param>
        /// <param name="duration">The duration of the timeout.</param>
        public delegate void UserTimeoutHandler(WinterBot sender, TwitchUser user, int duration);

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

        /// <summary>
        /// Event when a diagnostic event happens in the bot.
        /// </summary>
        /// <param name="sender">The bot sending the event.</param>
        /// <param name="facility">The type of event occurring.</param>
        /// <param name="message">The message.</param>
        public delegate void DiagnosticEventHandler(WinterBot sender, DiagnosticFacility facility, string message);

        /// <summary>
        /// Fired when the viewer count is updated.
        /// </summary>
        /// <param name="sender">The bot sending the event.</param>
        /// <param name="currentViewerCount">The new viewer total for the stream.</param>
        public delegate void ViewerEventHandler(WinterBot sender, int currentViewerCount);
        #endregion

        public DateTime LastMessageSent { get; private set; }

        /// <summary>
        /// Returns true if the stream is live, false otherwise (updates every 60 seconds).
        /// </summary>
        public bool IsStreamLive { get; private set; }

        /// <summary>
        /// Returns the total number of viewers who have ever watched the stream (updates every 60 seconds).
        /// </summary>
        public int TotalViewers { get; private set; }

        /// <summary>
        /// Returns the number of viewers currently watching the stream (updates every 60 seconds).
        /// </summary>
        public int CurrentViewers
        {
            get
            {
                return m_viewers;
            }
            private set
            {
                if (m_viewers != value)
                {
                    m_viewers = value;
                    var evt = ViewerCountChanged;
                    if (evt != null)
                    {
                        m_events.Enqueue(new Tuple<Delegate, object[]>(evt, new object[] { this, value }));
                        m_event.Set();
                    }
                }
            }
        }

        /// <summary>
        /// Returns the name of the game being played (updates every 60 seconds).
        /// </summary>
        public string Game { get; private set; }

        /// <summary>
        /// Returns the stream title (updates every 60 seconds).
        /// </summary>
        public string Title { get; private set; }

        public bool Silent { get; set; }
        public bool Quiet { get; set; }
        public bool Passive { get; set; }

        public Options Options { get { return m_options; } }

        public string Channel { get { return m_channel; } }

        public TwitchUsers Users
        {
            get
            {
                return m_data;
            }
        }

        public WinterBot(Options options, string channel, string user, string oauth)
        {
            m_data = new TwitchUsers(this);
            m_options = options;
            m_channel = channel.ToLower();

            Passive = m_options.Passive;

            MessageReceived += TryProcessCommand;
            LastMessageSent = DateTime.Now;

            m_twitch = new TwitchClient(m_data);
            m_twitch.InformChatClear += ClearChatHandler;
            m_twitch.MessageReceived += ChatMessageReceived;
            m_twitch.UserSubscribed += SubscribeHandler;
            m_twitch.InformModerator += InformModerator;

            LoadRegulars();
            LoadExtensions();
        }

        ~WinterBot()
        {
            if (m_streamLiveThread != null)
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            m_checkUpdates = false;
        }

        public void Ban(TwitchUser user)
        {
            if (Passive)
                return;

            var evt = UserBanned;
            if (evt != null)
                evt(this, user);

            m_twitch.Ban(user.Name);
        }

        public void ClearChat(TwitchUser user)
        {
            if (Passive)
                return;

            Timeout(user, 1);
        }

        public void Timeout(TwitchUser user, int duration = 600)
        {
            if (Passive)
                return;

            var evt = UserTimedOut;
            if (evt != null)
                evt(this, user, duration);

            m_twitch.Timeout(user.Name, duration);
        }


        private void LoadExtensions()
        {
            if (m_options.Regulars)
                AddCommands(new Regulars(this));

            var chatOptions = m_options.ChatOptions;
            if (chatOptions.SaveLog || chatOptions.SaveBinaryLog)
                AddCommands(new ChatLogger(this));

            AddCommands(new AutoMessage(this));
            AddCommands(new UserCommands(this));

            AddCommands(new TimeoutController(this));
            AddCommands(new Quiet(this));
        }

        public void AddCommands(object commands)
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

        private void AddCommand(string cmd, WinterBotCommand command, AccessLevel requiredAccess)
        {
            cmd = cmd.ToLower();
            m_commands[cmd] = new CmdValue(command, requiredAccess);
        }

        public void WriteDiagnostic(DiagnosticFacility facility, string msg)
        {
            var evt = DiagnosticMessage;
            if (evt != null)
                evt(this, facility, msg);
        }

        public void WriteDiagnostic(DiagnosticFacility facility, string msg, params object[] values)
        {
            WriteDiagnostic(facility, string.Format(msg, values));
        }

        public void SendResponse(string msg)
        {
            Send(MessageType.Response, msg);
        }

        public void SendResponse(string msg, params object[] param)
        {
            Send(MessageType.Response, string.Format(msg, param));
        }

        public void TimeoutMessage(string msg, params object[] param)
        {
            Send(MessageType.Timeout, string.Format(msg, param));
        }

        public void TimeoutMessage(string msg)
        {
            Send(MessageType.Timeout, msg);
        }


        public void SendMessage(string msg, params object[] param)
        {
            Send(MessageType.Message, string.Format(msg, param));
        }

        public void SendMessage(string msg)
        {
            Send(MessageType.Message, msg);
        }

        internal void SendUnconditional(string msg, params string[] args)
        {
            SendUnconditional(string.Format(msg, args));
        }

        internal void SendUnconditional(string msg)
        {
            if (Passive)
                return;

            m_twitch.SendMessage(msg);
            LastMessageSent = DateTime.Now;
        }


        public void Send(MessageType type, string msg)
        {
            if (Passive)
                return;

            if (!AllowMessage(type))
                return;

            m_twitch.SendMessage(msg);
            LastMessageSent = DateTime.Now;
        }

        public void Send(MessageType type, string fmt, params object[] param)
        {
            Send(type, string.Format(fmt, param));
        }

        private bool AllowMessage(MessageType type)
        {
            if (Silent || Passive)
                return false;

            switch (type)
            {
                case MessageType.Message:
                case MessageType.Response:
                    return !Quiet;
            }

            return true;
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
            var evt = ChatClear;
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

        void InformModerator(TwitchClient sender, TwitchUser user, bool moderator)
        {
            var evt = moderator ? ModeratorAdded : ModeratorRemoved;
            if (evt != null)
            {
                m_events.Enqueue(new Tuple<Delegate, object[]>(evt, new object[] { this, user }));
                m_event.Set();
            }
        }


        public void Shutdown()
        {
            var evt = BeginShutdown;
            if (evt != null)
                evt(this);

            evt = EndShutdown;
            if (evt != null)
                evt(this);

            m_twitch.Disconnect();
        }


        public void Go()
        {
            if (m_streamLiveThread == null)
            {
                m_streamLiveThread = new Thread(StreamLiveWoker);
                m_streamLiveThread.Start();
            }

            if (m_streamFollowerThread == null)
            {
                m_streamFollowerThread = new Thread(StreamFollowerWorker);
                m_streamFollowerThread.Start();
            }

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

                if (timer.Elapsed.TotalSeconds >= 5)
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

            cmd = cmd.ToLower();
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
                    return isStreamer || user.IsSubscriber || user.IsModerator || user.IsRegular;

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
            {
                var evt = Connected;
                if (evt != null)
                    evt(this);
            }

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
            return Path.Combine(Options.DataDirectory, m_channel + "_regulars.txt");
        }

        internal bool IsRegular(string user)
        {
            return m_regulars.Contains(user.ToLower());
        }

        private void StreamLiveWoker()
        {
            while (m_checkUpdates)
            {
                // Check stream values
                string url = @"http://api.justin.tv/api/stream/list.json?channel=" + m_channel;
                string result = GetUrl(url);
                if (result != null)
                {
                    var channels = JsonConvert.DeserializeObject<List<TwitchChannelResponse>>(result);
                    
                    bool live = channels.Count > 0;
                    if (live != IsStreamLive)
                    {
                        IsStreamLive = live;

                        var evt = live ? StreamOnline : StreamOffline;
                        if (evt != null)
                        {
                            m_events.Enqueue(new Tuple<Delegate, object[]>(evt, new object[] { this }));
                            m_event.Set();
                        }
                    }
                    

                    if (channels.Count > 0)
                    {
                        var channel = channels[0];
                        TotalViewers = channel.channel_view_count;
                        CurrentViewers = channel.channel_count;
                        Game = channel.meta_game;
                        Title = channel.title;
                        // TODO: Uptime = channel.up_time;
                    }
                    else
                    {
                        CurrentViewers = 0;
                    }
                }

                // One minute between updates.
                Thread.Sleep(60000);
            }
        }
        private void StreamFollowerWorker()
        {
            bool checkedFollowers = false;
            DateTime lastFollow = DateTime.Now;
            string url, result;

            while (m_checkUpdates)
            {
                var followedEvt = UserFollowed;
                if (followedEvt != null)
                {
                    // Check followers
                    if (!checkedFollowers)
                    {
                        url = string.Format("https://api.twitch.tv/kraken/channels/{0}/follows?direction=desc&limit=1&offset=0", m_channel); ;
                        var follow = JsonConvert.DeserializeObject<JsonFollows>(GetUrl(url));

                        lastFollow = DateTime.Parse(follow.follows[0].created_at);
                        checkedFollowers = true;
                    }
                    else
                    {
                        int count = 0;
                        int limit = 25;

                        do
                        {
                            url = string.Format("https://api.twitch.tv/kraken/channels/{0}/follows?direction=desc&offset={1}&limit={2}", m_channel, count, limit);
                            count += limit;

                            result = GetUrl(url);
                            if (result != null)
                            {
                                JsonFollows follows = JsonConvert.DeserializeObject<JsonFollows>(result);
                                foreach (var follow in follows.follows)
                                {
                                    DateTime last = DateTime.Parse(follow.created_at);
                                    if (last <= lastFollow)
                                        break;

                                    var user = Users.GetUser(follow.user.name);
                                    m_events.Enqueue(new Tuple<Delegate, object[]>(followedEvt, new object[] { this, user }));
                                }
                            }
                        } while (true);
                    }
                }

                Thread.Sleep(15000);
            }
        }

        private static string GetUrl(string url)
        {
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.HttpWebRequest.Create(url);
                req.UserAgent = "WinterBot/0.1.0.0";
                var response = req.GetResponse();
                var fromStream = response.GetResponseStream();

                StreamReader reader = new StreamReader(fromStream);
                string result = reader.ReadToEnd();
                return result;
            }
            catch (Exception)
            {
                // We ignore exceptions (mostly network issues), just leave the values alone for this iteration
            }

            return null;
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
