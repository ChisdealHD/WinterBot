using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Winter.BotEvents;
using WinterBotLogging;


namespace Winter
{
    public enum MessageType
    {
        Message,
        Response,
        Timeout
    }

    public delegate void WinterBotCommand(WinterBot sender, TwitchUser user, string cmd, string value);
    
    public abstract class WinterBotPlugin
    {
        public abstract void Initialize(WinterBot bot);
    }

    public enum DiagnosticFacility
    {
        UserError,
        Ban,
        Twitch,
        Error,
        IO,
        ModeChange,
        Network,
        Irc,
        Info
    }


    public class WinterBot
    {
        TwitchClient m_twitch;
        TwitchUsers m_data;
        ConcurrentQueue<WinterBotEvent> m_events = new ConcurrentQueue<WinterBotEvent>();
        AutoResetEvent m_event = new AutoResetEvent(false);
        string m_channel;
        bool m_shutdown;

        Dictionary<string, CmdValue> m_commands = new Dictionary<string, CmdValue>();
        private Options m_options;
        UserSet m_regulars;

        int m_viewers;
        bool m_checkingFollowers;

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
        /// Fired when a chat action is received (that's /me in chat).
        /// </summary>
        public event MessageHandler ActionReceived;

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
        /// Called when the bot is disconnected from the Twitch servers.  Note the bot
        /// will automatically attempt to reconnect to the server.
        /// </summary>
        public event BotEventHandler Disconnected;

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
                    if (ViewerCountChanged != null)
                    {
                        m_events.Enqueue(new ViewerCountEvent(value));
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
            m_options = options;
            m_channel = channel.ToLower();
            m_data = new TwitchUsers(this);

            Passive = m_options.Passive;

            MessageReceived += TryProcessCommand;
            LastMessageSent = DateTime.Now;

            LoadExtensions();
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
            {
                m_regulars = new UserSet(this, "regulars");
                AddCommands(new Regulars(this));
            }

            ChatLog.Init(this);

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

        #region Messages
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

        public void SendResponse(Importance imp, string msg)
        {
            Send(MessageType.Response, imp, msg);
        }

        public void SendResponse(Importance imp, string msg, params object[] param)
        {
            Send(MessageType.Response, imp, string.Format(msg, param));
        }

        public void SendResponse(string msg, params object[] param)
        {
            Send(MessageType.Response, Importance.Med, string.Format(msg, param));
        }

        public void SendMessage(Importance imp, string msg, params object[] param)
        {
            Send(MessageType.Message, imp, string.Format(msg, param));
        }

        public void SendMessage(string msg, params object[] param)
        {
            Send(MessageType.Message, Importance.Low, string.Format(msg, param));
        }

        public void SendMessage(Importance imp, string msg)
        {
            Send(MessageType.Message, imp, msg);
        }

        public void SendMessage(string msg)
        {
            Send(MessageType.Message, Importance.Low, msg);
        }

        internal void SendUnconditional(string msg, params string[] args)
        {
            SendUnconditional(string.Format(msg, args));
        }

        internal void SendUnconditional(string msg)
        {
            if (Passive)
                return;

            m_twitch.SendMessage(Importance.High, msg);
            LastMessageSent = DateTime.Now;
        }


        public void Send(MessageType type, Importance imp, string msg)
        {
            if (Passive)
                return;

            if (!AllowMessage(type))
                return;

            m_twitch.SendMessage(imp, msg);
            LastMessageSent = DateTime.Now;
        }

        public void Send(MessageType type, Importance imp, string fmt, params object[] param)
        {
            Send(type, imp, string.Format(fmt, param));
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
        #endregion


        #region Event Wrappers
        void UserFollowedHandler(string channel, IEnumerable<string> users)
        {
            var evt = UserFollowed;
            if (evt != null)
            {
                foreach (var user in users)
                {
                    if (TwitchUsers.IsValidUserName(user))
                        evt(this, Users.GetUser(user));
                }
            }
        }

        void ChannelDataReceived(string channelName, List<TwitchChannelResponse> result)
        {
            Debug.Assert(channelName.Equals(m_channel, StringComparison.CurrentCultureIgnoreCase));

            bool live = result.Count > 0;
            if (live != IsStreamLive)
            {
                IsStreamLive = live;

                var evt = live ? StreamOnline : StreamOffline;
                if (evt != null)
                {
                    m_events.Enqueue(new StreamStatusEvent(live));
                    m_event.Set();
                }
            }

            if (result.Count > 0)
            {
                var channel = result[0];
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

        private void OnConnected()
        {
            var evt = Connected;
            if (evt != null)
                evt(this);
        }

        void OnDisconnected()
        {
            var evt = Disconnected;
            if (evt != null)
                evt(this);
        }

        void OnStreamStatus(StreamStatusEvent status)
        {
            var evt = status.Online ? StreamOnline : StreamOffline;
            if (evt != null)
                evt(this);
        }

        void OnUserFollowed(FollowEvent follow)
        {
            var evt = UserFollowed;
            if (evt != null)
                evt(this, follow.User);
        }

        void OnViewerCountChanged(ViewerCountEvent args)
        {
            var evt = ViewerCountChanged;
            if (evt != null)
                evt(this, args.Viewers);
        }

        
        void OnClear(ClearEvent clr)
        {
            var evt = ChatClear;
            if (evt != null)
                evt(this, clr.User);
        }

        void OnMessage(MessageEvent msg)
        {
            var evt = MessageReceived;
            if (evt != null)
                evt(this, msg.User, msg.Text);
        }

        void OnAction(ActionEvent ae)
        {
            var evt = ActionReceived;
            if (evt != null)
                evt(this, ae.User, ae.Text);
        }

        void OnMod(ModEvent mod)
        {
            var evt = mod.Mod ? ModeratorAdded : ModeratorRemoved;
            if (evt != null)
                evt(this, mod.User);
        }

        void OnSub(SubscribeEvent sub)
        {
            var evt = UserSubscribed;
            if (evt != null)
                evt(this, sub.User);
        }
        #endregion

        #region Giant Switch Statement
        private void IrcStatusHandler(TwitchClient sender, string message)
        {
            WriteDiagnostic(DiagnosticFacility.Irc, message);
        }

        private void ChatActionReceived(TwitchClient source, TwitchUser user, string text)
        {
            if (ActionReceived != null)
            {
                m_events.Enqueue(new ActionEvent(user, text));
                m_event.Set();
            }
        }

        private void ChatMessageReceived(TwitchClient source, TwitchUser user, string text)
        {
            if (MessageReceived != null)
            {
                m_events.Enqueue(new MessageEvent(user, text));
                m_event.Set();
            }
        }

        private void ClearChatHandler(TwitchClient source, TwitchUser user)
        {
            if (ChatClear != null)
            {
                m_events.Enqueue(new ClearEvent(user));
                m_event.Set();
            }
        }

        private void SubscribeHandler(TwitchClient source, TwitchUser user)
        {
            if (UserSubscribed != null)
            {
                m_events.Enqueue(new SubscribeEvent(user));
                m_event.Set();
            }
        }

        void InformModerator(TwitchClient sender, TwitchUser user, bool moderator)
        {
            var evt = moderator ? ModeratorAdded : ModeratorRemoved;
            if (evt != null)
            {
                m_events.Enqueue(new ModEvent(user, moderator));
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

            m_twitch.Quit();
            m_shutdown = true;
        }

        void Connect()
        {
            if (m_twitch != null)
            {
                m_twitch.InformChatClear -= ClearChatHandler;
                m_twitch.MessageReceived -= ChatMessageReceived;
                m_twitch.ActionReceived -= ChatActionReceived;
                m_twitch.UserSubscribed -= SubscribeHandler;
                m_twitch.InformModerator -= InformModerator;
                m_twitch.StatusUpdate -= IrcStatusHandler;
            }

            m_twitch = new TwitchClient(m_data);
            m_twitch.InformChatClear += ClearChatHandler;
            m_twitch.MessageReceived += ChatMessageReceived;
            m_twitch.ActionReceived += ChatActionReceived;
            m_twitch.UserSubscribed += SubscribeHandler;
            m_twitch.InformModerator += InformModerator;
            m_twitch.StatusUpdate += IrcStatusHandler;

            bool first = true;
            ConnectResult result;
            const int sleepTime = 5000;
            do
            {
                if (!NativeMethods.IsConnectedToInternet())
                {
                    WriteDiagnostic(DiagnosticFacility.Network, "Not connected to the internet.");

                    do
                    {
                        Thread.Sleep(sleepTime);
                    } while (!NativeMethods.IsConnectedToInternet());

                    WriteDiagnostic(DiagnosticFacility.Network, "Re-connected to the internet.");
                }

                if (!first)
                    Thread.Sleep(sleepTime);

                first = false;
                result = m_twitch.Connect(m_channel, m_options.Username, m_options.Password);

                if (result == ConnectResult.LoginFailed)
                        throw new TwitchLoginException(m_options.Username);

            } while (result != ConnectResult.Success);

            OnConnected();
        }

        public void Go()
        {
            Thread.CurrentThread.Name = "WinterBot Event Loop";
            Connect();

            WinterBotSource.Log.Connected(m_channel);

            TwitchHttp.Instance.ChannelDataReceived += ChannelDataReceived;
            TwitchHttp.Instance.PollChannelData(m_channel);

            DateTime lastTick = DateTime.Now;
            DateTime lastPing = DateTime.Now;

            while (!m_shutdown)
            {
                if (m_events.Count == 0)
                    m_event.WaitOne(200);

                WinterBotEvent evt;
                while (m_events.TryDequeue(out evt))
                {
                    switch (evt.Event)
                    {
                        case EventType.Action:
                            ActionEvent action = (ActionEvent)evt;
                            WinterBotSource.Log.BeginAction(action.User.Name, action.Text);

                            OnAction(action);

                            WinterBotSource.Log.EndAction();
                            break;

                        case EventType.Clear:
                            var clear = (ClearEvent)evt;
                            WinterBotSource.Log.BeginClear(clear.User.Name);
                            
                            OnClear(clear);

                            WinterBotSource.Log.EndClear();
                            break;

                        case EventType.Message:
                            var msg = (MessageEvent)evt;
                            WinterBotSource.Log.BeginMessage(msg.User.Name, msg.Text);

                            OnMessage(msg);

                            WinterBotSource.Log.EndMessage();
                            break;

                        case EventType.Mod:
                            var mod = (ModEvent)evt;
                            WinterBotSource.Log.BeginMod(mod.User.Name, mod.Mod);

                            OnMod(mod);

                            WinterBotSource.Log.EndMod();
                            break;

                        case EventType.Subscribe:
                            var sub = (SubscribeEvent)evt;
                            WinterBotSource.Log.BeginSub(sub.User.Name);

                            OnSub(sub);

                            WinterBotSource.Log.EndSub();
                            break;

                        case EventType.ViewerCount:
                            var viewer = (ViewerCountEvent)evt;
                            WinterBotSource.Log.BeginViewers(viewer.Viewers);

                            OnViewerCountChanged(viewer);

                            WinterBotSource.Log.EndViewers();
                            break;

                        case EventType.StreamStatus:
                            var status = (StreamStatusEvent)evt;
                            WinterBotSource.Log.BeginStreamStatus(status.Online);

                            OnStreamStatus(status);

                            WinterBotSource.Log.EndStreamStatus();
                            break;

                        case EventType.Follow:
                            var follow = (FollowEvent)evt;
                            WinterBotSource.Log.BeginFollow(follow.User.Name);

                            OnUserFollowed(follow);

                            WinterBotSource.Log.EndFollow();
                            break;

                        default:
                            Debug.Fail("No handler for event!");
                            break;
                    }
                }

                var elapsed = lastTick.Elapsed();
                if (elapsed.TotalSeconds >= 5)
                {
                    WinterBotSource.Log.BeginTick();

                    OnTick(elapsed);
                    lastTick = DateTime.Now;

                    WinterBotSource.Log.EndTick();

                    bool followedListeners = UserFollowed != null;
                    if (m_checkingFollowers != followedListeners)
                    {
                        if (m_checkingFollowers)
                        {
                            m_checkingFollowers = false;
                            TwitchHttp.Instance.StopPollingFollowers(m_channel);
                            TwitchHttp.Instance.UserFollowed -= UserFollowedHandler;
                        }
                        else
                        {
                            m_checkingFollowers = true;
                            TwitchHttp.Instance.PollFollowers(m_channel);
                            TwitchHttp.Instance.UserFollowed += UserFollowedHandler;
                        }
                    }
                }

                const int pingDelay = 20;
                var lastEvent = m_twitch.LastEvent;
                if (lastEvent.Elapsed().TotalSeconds >= pingDelay && lastPing.Elapsed().TotalSeconds >= pingDelay)
                {
                    m_twitch.Ping();
                    lastPing = DateTime.Now;
                }

                if (lastEvent.Elapsed().TotalMinutes >= 1)
                {
                    WinterBotSource.Log.BeginReconnect();

                    m_twitch.Quit(250);
                    OnDisconnected();

                    Connect();

                    WinterBotSource.Log.EndReconnect();
                }
            }

            TwitchHttp.Instance.StopPolling(m_channel);
            TwitchHttp.Instance.ChannelDataReceived -= ChannelDataReceived;
            if (m_checkingFollowers)
                TwitchHttp.Instance.UserFollowed -= UserFollowedHandler;
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
                {
                    WinterBotSource.Log.DenyCommand(user.Name, cmd, "access");
                    return;
                }


                WinterBotSource.Log.BeginCommand(user.Name, cmd, value);
                command.Command(this, user, cmd, value);
                WinterBotSource.Log.EndCommand();
            }
            else
            {
                WinterBotSource.Log.BeginUnknownCommand(user.Name, cmd, value);
                OnUnknownCommand(user, cmd, value);
                WinterBotSource.Log.EndUnknownCommand();
            }
        }

        public bool CanUseCommand(TwitchUser user, AccessLevel required)
        {
            return user.Access >= required;
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

        internal void AddRegular(TwitchUser user)
        {
            if (m_regulars != null)
                m_regulars.Add(user);
        }

        internal void RemoveRegular(TwitchUser user)
        {
            if (m_regulars != null)
                m_regulars.Remove(user);
        }


        internal bool IsRegular(TwitchUser user)
        {
            return m_regulars != null ? m_regulars.Contains(user) : false;
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


    [AttributeUsage(AttributeTargets.Class)]
    public class WinterBotPluginAttribute : Attribute
    {
    }
}

public class TwitchLoginException : Exception
{
    public TwitchLoginException(string user)
        :base("Login failed for twitch user: " + user)
    {
    }
}
