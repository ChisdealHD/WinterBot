using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Winter;
using WinterBotLogging;

namespace TwitchChat
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        bool m_playSounds, m_confirmTimeouts, m_confirmBans, m_highlightQuestions, m_showIcons;
        Thread m_thread;
        ChatOptions m_options;
        TwitchClient m_twitch;
        TwitchUsers m_users;
        string m_channel;

        public event PropertyChangedEventHandler PropertyChanged;
        bool m_reconnect;

        SoundPlayer m_subSound = new SoundPlayer(Properties.Resources.Subscriber);
        
        public MainWindow()
        {
            if (!Debugger.IsAttached)
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            m_options = new ChatOptions();
            m_playSounds = m_options.GetOption("PlaySounds", true);
            m_highlightQuestions = m_options.GetOption("HighlightQuestions", true);
            m_confirmBans = m_options.GetOption("ConfirmBans", true);
            m_confirmTimeouts = m_options.GetOption("ConfirmTimeouts", false);
            m_showIcons = m_options.GetOption("ShowIcons", true);
            m_channel = m_options.Stream.ToLower();
            TwitchHttp.Instance.PollChannelData(m_channel);
            TwitchHttp.Instance.ChannelDataReceived += Instance_ChannelDataReceived;

            m_thread = new Thread(ThreadProc);
            m_thread.Start();

            Messages = new ObservableCollection<ChatItem>();

            InitializeComponent();
            Channel.Text = m_channel;
        }

        void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                // This will drop an "error.txt" file if we encounter an unhandled exception.
                string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                Exception obj = (Exception)e.ExceptionObject;
                string text = string.Format("{0}: {1}\n{2}", obj.GetType().ToString(), obj.Message, obj.StackTrace.ToString());

                using (var file = File.CreateText(System.IO.Path.Combine(myDocuments, "winterbot_error.txt")))
                    file.WriteLine(text);

                MessageBox.Show(text, "Unhandled Exception");
            }
            catch
            {
                // Ignore errors here.
            }
        }

        public void Unban(TwitchUser user)
        {
            m_twitch.Unban(user.Name);
            WriteStatus(string.Format("Unbanned user {0}.", user.Name));
        }

        public bool Ban(TwitchUser user)
        {
            MessageBoxResult res = MessageBoxResult.Yes;
            if (ConfirmBans)
                res = MessageBox.Show(string.Format("Ban user {0}?", user.Name), "Ban User", MessageBoxButton.YesNo);

            if (res == MessageBoxResult.Yes)
            {
                m_twitch.Ban(user.Name);
                WriteStatus(string.Format("Banned user {0}.", user.Name));
                return true;
            }

            return false;
        }

        public bool Timeout(TwitchUser user, int duration)
        {
            if (duration <= 0)
                duration = 1;

            MessageBoxResult res = MessageBoxResult.Yes;
            if (ConfirmTimeouts && duration > 1)
                res = MessageBox.Show(string.Format("Timeout user {0}?", user.Name), "Timeout User", MessageBoxButton.YesNo);

            if (res == MessageBoxResult.Yes)
            {
                m_twitch.Timeout(user.Name, duration);

                if (duration > 1)
                    WriteStatus(string.Format("Timed out user {0} for {1} seconds", user.Name, duration));
                else
                    WriteStatus(string.Format("Purged user {0}.", user.Name));

                return true;
            }

            return false;
        }

        public void ThreadProc()
        {
            m_twitch = new TwitchClient();

            if (!Connect())
                return;

            const int pingDelay = 20;
            DateTime lastPing = DateTime.UtcNow;
            DateTime lastPurge = DateTime.UtcNow;
            while (true)
            {
                Thread.Sleep(1000);

                var lastEvent = m_twitch.LastEvent;
                if (lastEvent.Elapsed().TotalSeconds >= pingDelay && lastPing.Elapsed().TotalSeconds >= pingDelay)
                {
                    m_twitch.Ping();
                    lastPing = DateTime.UtcNow;
                }

                if (lastEvent.Elapsed().TotalMinutes >= 1)
                {
                    WinterBotSource.Log.BeginReconnect();
                    WriteStatus("Disconnected!");

                    m_twitch.Quit(250);

                    if (!Connect())
                        return;

                    WinterBotSource.Log.EndReconnect();
                }
                else if (m_reconnect)
                {
                    m_reconnect = false;
                    if (!Connect())
                        return;
                }


                if (lastPurge.Elapsed().TotalMinutes >= 5)
                {
                    lastPurge = DateTime.UtcNow;
                    Dispatcher.Invoke(DispatcherPriority.Normal, new Action(ClearMessages));
                }
            }
        }

        private void ClearMessages()
        {
            DateTime now = DateTime.UtcNow;
            while (Messages.Count > 500 && (now - Messages[0].Time).TotalMinutes >= 5)
                Messages.RemoveAt(0);
        }


        bool Connect()
        {
            if (m_twitch != null)
            {
                m_twitch.InformChatClear -= ClearChatHandler;
                m_twitch.MessageReceived -= ChatMessageReceived;
                m_twitch.ActionReceived -= ChatActionReceived;
                m_twitch.UserSubscribed -= SubscribeHandler;
                m_twitch.StatusUpdate -= StatusUpdate;
            }

            string channel = m_channel.ToLower();
            m_users = new TwitchUsers(channel);
            m_twitch = new TwitchClient(m_users);
            m_twitch.InformChatClear += ClearChatHandler;
            m_twitch.MessageReceived += ChatMessageReceived;
            m_twitch.ActionReceived += ChatActionReceived;
            m_twitch.UserSubscribed += SubscribeHandler;
            m_twitch.StatusUpdate += StatusUpdate;

            bool first = true;
            ConnectResult result;
            const int sleepTime = 5000;
            do
            {
                if (!NativeMethods.IsConnectedToInternet())
                {
                    WriteStatus("Not connected to the internet.");

                    do
                    {
                        Thread.Sleep(sleepTime);
                    } while (!NativeMethods.IsConnectedToInternet());

                    WriteStatus("Re-connected to the internet.");
                }

                if (!first)
                    Thread.Sleep(sleepTime);

                first = false;
                result = m_twitch.Connect(channel, m_options.User, m_options.Pass);

                if (result == ConnectResult.LoginFailed)
                {
                    WriteStatus("Failed to login, please change options.ini and restart the application.");
                    return false;
                }
                else if (result != ConnectResult.Success)
                {
                    WriteStatus("Failed to connect: {0}", result == ConnectResult.NetworkFailed ? "network failed" : "failed");
                }
            } while (result != ConnectResult.Success);

            WriteStatus("Connected to channel {0}.", channel);
            return true;
        }

        private void StatusUpdate(TwitchClient sender, string message)
        {
            WriteStatus(message);
        }

        #region Event Handlers
        private void SubscribeHandler(TwitchClient sender, TwitchUser user)
        {
            if (PlaySounds)
                m_subSound.Play();

            Dispatcher.Invoke(DispatcherPriority.Normal, new Action<TwitchUser>(DispatcherUserSubscribed), user);
        }

        private void ChatActionReceived(TwitchClient sender, TwitchUser user, string text)
        {
            if (m_options.Ignore.Contains(user.Name))
                return;

            Dispatcher.Invoke(DispatcherPriority.Normal, new Action<ChatItem>(AddItem), new ChatAction(this, user, text));
        }

        private void ChatMessageReceived(TwitchClient sender, TwitchUser user, string text)
        {
            if (m_options.Ignore.Contains(user.Name))
                return;

            bool question = false;
            if (HighlightQuestions)
            {
                foreach (var highlight in m_options.Highlights)
                {
                    if (text.ToLower().Contains(highlight))
                    {
                        question = true;
                        break;
                    }
                }
            }

            Dispatcher.Invoke(DispatcherPriority.Normal, new Action<ChatItem>(AddItem), new ChatMessage(this, user, text, question));
        }


        void Instance_ChannelDataReceived(string arg1, List<TwitchChannelResponse> arg2)
        {
            string text = "OFFLINE";
            if (arg2.Count == 1)
                text = arg2[0].channel_count.ToString() + " viewers";

            Dispatcher.Invoke(DispatcherPriority.Normal, new Action<string>(SetViewers), text);
        }

        private void SetViewers(string value)
        {
            Viewers.Content = value;
        }


        private void ClearChatHandler(TwitchClient sender, TwitchUser user)
        {
            Dispatcher.Invoke(DispatcherPriority.Normal, new Action<TwitchUser>(DispatcherClearChat), user);
        }

        void WriteStatus(string msg, params string[] args)
        {
            if (args.Length > 0)
                msg = string.Format(msg, args);

            Dispatcher.Invoke(DispatcherPriority.Normal, new Action<ChatItem>(AddItem), new StatusMessage(this, msg));
        }


        private void DispatcherClearChat(TwitchUser user)
        {
            foreach (var msg in Messages)
            {
                if (msg.User != user)
                    continue;

                msg.ClearChat();
            }
        }



        private void DispatcherUserSubscribed(TwitchUser user)
        {
            AddItem(new Subscriber(this, user));
        }

        void AddItem(ChatItem item)
        {
            bool gotoEnd = ScrollBar.VerticalOffset == ScrollBar.ScrollableHeight;
            Messages.Add(item);
            if (gotoEnd)
                ScrollBar.ScrollToEnd();
        }
        #endregion

        #region UI Properties
        protected void OnPropertyChanged(string name)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
                handler(this, new PropertyChangedEventArgs(name));
        }

        public ObservableCollection<ChatItem> Messages { get; set; }
        
        public bool PlaySounds
        {
            get
            {
                return m_playSounds;
            }
            set
            {
                if (m_playSounds != value)
                {
                    m_options.SetOption("PlaySounds", value);
                    m_playSounds = value;
                    OnPropertyChanged("PlaySounds");
                }
            }
        }

        public bool ConfirmTimeouts
        {
            get
            {
                return m_confirmTimeouts;
            }
            set
            {
                if (m_confirmTimeouts != value)
                {
                    m_options.SetOption("ConfirmTimeouts", value);
                    m_confirmTimeouts = value;
                    OnPropertyChanged("ConfirmTimeouts");
                }
            }
        }

        public bool ConfirmBans
        {
            get
            {
                return m_confirmBans;
            }
            set
            {
                if (m_confirmBans != value)
                {
                    m_options.SetOption("ConfirmBans", value);
                    m_confirmBans = value;
                    OnPropertyChanged("ConfirmBans");
                }
            }
        }


        public bool ShowIcons
        {
            get
            {
                return m_showIcons;
            }
            set
            {
                if (m_showIcons != value)
                {
                    m_options.SetOption("ShowIcons", value);
                    m_showIcons = value;
                    OnPropertyChanged("ShowIcons");
                }
            }
        }

        public bool HighlightQuestions
        {
            get
            {
                return m_highlightQuestions;
            }
            set
            {
                if (m_highlightQuestions != value)
                {
                    m_options.SetOption("HighlightQuestions", value);
                    m_highlightQuestions = value;
                    OnPropertyChanged("HighlightQuestions");
                }
            }
        }
        #endregion

        #region Event Handlers
        private void Window_Closed(object sender, EventArgs e)
        {
            m_twitch.Quit();
            Application.Current.Shutdown();
            Environment.Exit(0);
        }

        private void OnReconnect(object sender, RoutedEventArgs e)
        {
            m_reconnect = true;
        }

        private void OnClear(object sender, RoutedEventArgs e)
        {
            Messages.Clear();
        }

        private void Channel_TextChanged(object sender, TextChangedEventArgs e)
        {
            TwitchHttp.Instance.StopPolling();
            m_channel = Channel.Text.ToLower();
            TwitchHttp.Instance.PollChannelData(m_channel);
        }
        private void AllItems_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            ScrollBar.ScrollToVerticalOffset(ScrollBar.VerticalOffset - e.Delta);
            e.Handled = true;
        }
        private void Chat_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && e.KeyboardDevice.Modifiers != ModifierKeys.Shift)
            {
                e.Handled = true;

                string text = Chat.Text;
                text = text.Replace('\n', ' ');
                m_twitch.SendMessage(Importance.High, text);

                Chat.Text = "";
                
                var user = m_twitch.ChannelData.GetUser(m_options.User);
                AddItem(new ChatMessage(this, ItemType.Message, user, text));
                return;
            }
        }
        #endregion
    }
}
