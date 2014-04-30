using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using TwitchChat.Properties;

namespace TwitchChat
{

    public class ChatLine : TextBlock
    {
        static BitmapImage s_sub, s_timeout, s_ban, s_eight, s_logs, s_check;
        public static readonly DependencyProperty ItemsProperty =
            DependencyProperty.Register(
                "Value",
                typeof(ChatItem),
                typeof(ChatLine),
                new PropertyMetadata(default(ChatItem), OnItemsPropertyChanged));

        Run m_message;


        TimeOutIcon m_timeout, m_eight, m_ban;
        Winter.TwitchUser m_user;

        MainWindow Controller { get { return Value != null ? Value.Controller : null; } }


        static ChatLine()
        {
            s_sub = GetBitmapImage(TwitchChat.Properties.Resources.star);
            s_timeout = GetBitmapImage(TwitchChat.Properties.Resources.timeout);
            s_ban = GetBitmapImage(TwitchChat.Properties.Resources.ban);
            s_eight = GetBitmapImage(TwitchChat.Properties.Resources.eight);
            s_logs = GetBitmapImage(TwitchChat.Properties.Resources.logs);
            s_check = GetBitmapImage(TwitchChat.Properties.Resources.check);
        }

        private static BitmapImage GetBitmapImage(System.Drawing.Bitmap bitmap)
        {
            using (MemoryStream memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;

                BitmapImage bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                return bitmapImage;
            }
        }

        private static void OnItemsPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
        }

        public ChatItem Value
        {
            get
            {
                return (ChatItem)GetValue(ItemsProperty);
            }
            set
            {
                SetValue(ItemsProperty, value);
            }
        }


        public ChatLine()
        {
            this.Initialized += ChatLine_Initialized;
            ContextMenu = new ContextMenu();
            ContextMenu.Opened += ContextMenu_Opened;
        }

        void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (ContextMenu.Items.Count > 0)
                return;

            if (m_user == null)
            {
                AddMenuItem("Copy", null, copy_line);
                e.Handled = true;
                return;
            }

            AddMenuItem("Copy", null, copy_line);
            AddMenuItem("Chat Logs", s_logs, showlogs_Click);
            AddMenuItem("Purge", null, purge_click);
            AddMenuItem("Timeout", s_timeout, timeout_click);
            AddMenuItem("8 Hour Timeout", s_eight, eight_click);
            AddMenuItem("Ban", s_ban, ban_click);
        }

        private void copy_line(object sender, RoutedEventArgs e)
        {
            string text = null;
            if (Value is ChatMessage)
                text = ((ChatMessage)Value).Message;
            else if (Value is StatusMessage)
                text = ((StatusMessage)Value).Message;

            if (text != null)
                Clipboard.SetText(text);
        }

        private void showlogs_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(string.Format("www.darkautumn.net/winter/chat.php?CHANNEL={0}&USER={1}", Controller.Channel.Text.ToLower(), m_user.Name));
        }

        private void timeout_click(object sender, RoutedEventArgs e)
        {
            if (Controller.Timeout(m_user, 600) && m_timeout != null)
                m_timeout.ShowAlternate();
        }

        private void eight_click(object sender, RoutedEventArgs e)
        {
            if (Controller.Timeout(m_user, 28800) && m_eight != null)
                m_eight.ShowAlternate();
        }

        private void ban_click(object sender, RoutedEventArgs e)
        {
            if (Controller.Ban(m_user) && m_ban != null)
                m_ban.ShowAlternate();
        }

        private void purge_click(object sender, RoutedEventArgs e)
        {
            Controller.Timeout(m_user, 1);
        }

        private void AddMenuItem(string title, BitmapImage image, RoutedEventHandler handler)
        {
            MenuItem item = new MenuItem();
            item.Header = title;
            item.Click += handler;
            if (image != null)
                item.Icon = GetImage(image);
            ContextMenu.Items.Add(item);
        }

        void ChatLine_Initialized(object sender, EventArgs e)
        {
            var value = Value;
            if (value == null)
                return;

            value.Clear += value_Clear;

            switch (value.Type)
            {
                case ItemType.Action:
                case ItemType.Question:
                case ItemType.Message:
                    SetMessage((ChatMessage)value);
                    break;

                case ItemType.Status:
                    SetStatus((StatusMessage)value);
                    break;

                case ItemType.Subscriber:
                    SetSubscriber((Subscriber)value);
                    break;

                default:
                    Debug.Assert(false);
                    break;
            }
        }

        void value_Clear()
        {
            if (m_message == null)
                return;

            m_message.TextDecorations.Add(System.Windows.TextDecorations.Strikethrough);
            m_message.Foreground = Brushes.Gray;
        }

        private void SetSubscriber(Subscriber subscriber)
        {
            Text = string.Format("{0} just subscribed!", subscriber.User.Name);
            Foreground = Brushes.Red;
        }

        private void SetStatus(StatusMessage statusMessage)
        {
            Text = statusMessage.Message;
            Foreground = Brushes.Green;
        }

        private void SetMessage(ChatMessage msg)
        {
            m_user = msg.User;
            this.VerticalAlignment = System.Windows.VerticalAlignment.Center;

            m_ban = new TimeOutIcon(GetImage(s_ban), GetImage(s_check));
            m_ban.Clicked += m_ban_Clicked;
            m_ban.Restore += Unban;

            m_eight = new TimeOutIcon(GetImage(s_eight), GetImage(s_check));
            m_eight.Clicked += m_eight_Clicked;
            m_eight.Restore += Unban;

            m_timeout = new TimeOutIcon(GetImage(s_timeout), GetImage(s_check));
            m_timeout.Clicked += m_timeout_Clicked;
            m_timeout.Restore += Unban;

            if (Controller.ShowIcons)
            {
                Inlines.Add(m_ban);
                Inlines.Add(m_eight);
                Inlines.Add(m_timeout);
            }

            var user = msg.User;
            if (user.IsSubscriber)
                Inlines.Add(GetImage(s_sub));

            Brush userColor = Brushes.Black;
            if (user.Color != null)
            {
                try
                {
                    userColor = (SolidColorBrush)(new BrushConverter().ConvertFrom(msg.User.Color));
                }
                catch (NotSupportedException)
                {
                }
            }

            Inlines.Add(new Run(user.Name) { FontWeight = FontWeights.Bold, Foreground = userColor, BaselineAlignment = BaselineAlignment.Center });

            if (msg.Type != ItemType.Action)
                Inlines.Add(new Run(": ") { BaselineAlignment = BaselineAlignment.Center });

            if (msg.Type == ItemType.Question)
            {
                m_message = new Run(msg.Message) { FontWeight = FontWeights.Bold, BaselineAlignment = BaselineAlignment.Center };
                Inlines.Add(m_message);
            }
            else
            {
                m_message = new Run(msg.Message) { BaselineAlignment = BaselineAlignment.Center };
                Inlines.Add(m_message);
            }
        }

        void m_timeout_Clicked(TimeOutIcon obj)
        {
            if (m_user != null)
                if (Controller.Timeout(m_user, 600))
                    m_timeout.ShowAlternate();
        }

        void m_eight_Clicked(TimeOutIcon obj)
        {
            if (m_user != null)
                if (Controller.Timeout(m_user, 28800))
                    m_eight.ShowAlternate();
        }

        void Unban(TimeOutIcon icon)
        {
            Controller.Unban(m_user);
            icon.ShowIcon();
        }

        void m_ban_Clicked(TimeOutIcon icon)
        {
            if (m_user != null)
                if (Controller.Ban(m_user))
                    m_ban.ShowAlternate();
        }



        private static Image GetImage(BitmapImage bitmap)
        {
            Image img = new Image();
            img.Source = bitmap;
            img.Width = 18;
            img.Height = 18;
            img.VerticalAlignment = VerticalAlignment.Bottom;
            return img;
        }
    }

    class TimeOutIcon : InlineUIContainer
    {
        UIElement m_icon, m_check;

        public event Action<TimeOutIcon> Clicked;
        public event Action<TimeOutIcon> Restore;
        
        public TimeOutIcon(UIElement icon, UIElement check)
            : base(icon)
        {
            m_icon = icon;
            m_check = check;

            this.MouseUp += TimeOutIcon_MouseUp;
        }

        void TimeOutIcon_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            Click();
        }

        public void Click()
        {
            if (Child == m_icon)
            {
                var evt = Clicked;
                if (evt != null)
                    evt(this);
            }
            else
            {
                var evt = Restore;
                if (evt != null)
                    evt(this);
            }
        }

        public void ShowAlternate()
        {
            Child = m_check;
        }
        public void ShowIcon()
        {
            Child = m_icon;
        }
    }
}
