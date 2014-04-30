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


        InlineUIContainer m_timeout, m_eight, m_ban;

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
            this.VerticalAlignment = System.Windows.VerticalAlignment.Center;
            m_ban = new InlineUIContainer(GetImage(s_ban));
            m_ban.MouseUp += ban_MouseUp;
            Inlines.Add(m_ban);

            m_eight = new InlineUIContainer(GetImage(s_eight));
            m_eight.MouseUp += eight_MouseUp;
            Inlines.Add(m_eight);

            m_timeout = new InlineUIContainer(GetImage(s_timeout));
            m_timeout.MouseUp += timeout_MouseUp;
            Inlines.Add(m_timeout);

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

        private void timeout_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var value = Value;
            var user = value != null ? value.User : null;

            if (user != null)
            {
                if (Controller.Timeout(user, 600))
                    m_timeout.Child = GetImage(s_check);
            }
        }

        private void eight_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var value = Value;
            var user = value != null ? value.User : null;

            if (user != null)
            {
                if (Controller.Timeout(user, 28800))
                    m_eight.Child = GetImage(s_check);
            }
        }

        private void ban_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            var value = Value;
            var user = value != null ? value.User : null;

            if (user != null)
            {
                if (Controller.Ban(user))
                    m_ban.Child = GetImage(s_check);
            }
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
}
