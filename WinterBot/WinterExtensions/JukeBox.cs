using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Winter;

namespace WinterExtensions
{
    public class JukeBox
    {
        bool m_enabled;
        Stopwatch m_lastMessage = new Stopwatch();
        string m_message = " Tonight we are doing song requests! For a $2.50 donation at http://streamdonations.net/c/zlfreebird you can have a single song played! Just link to youtube in the message, and keep it less than 6 minutes.";

        public JukeBox(WinterBot bot)
        {
            bot.Tick += bot_Tick;
        }

        void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            if (m_enabled)
            {
                if (!sender.IsStreamLive)
                {
                    m_enabled = false;
                    sender.SendMessage("Disabling jukebox mode.");
                    m_lastMessage.Stop();
                }
            }

            if (m_enabled && m_lastMessage.Elapsed.TotalMinutes >= 7)
                SendMessage(sender);
        }
        
        [BotCommand(AccessLevel.Normal, "jukebox")]
        public void ListCommands(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!m_enabled)
            {
                if (!sender.CanUseCommand(user, AccessLevel.Mod))
                    return;

                value = value.Trim().ToLower();

                if (value == "on")
                {
                    m_enabled = true;
                    m_lastMessage.Restart();
                    sender.SendMessage("Jukebox mode enabled!  Use '!jukebox off' to turn it off.");
                }
                else if (value == "off")
                {
                    sender.SendMessage("Jukebox mode is off.");
                }
                else
                {
                    sender.SendMessage("Usage: '!jukebox on' and '!jukebox off'.  Mod only.");
                }
            }
            else
            {
                if (sender.CanUseCommand(user, AccessLevel.Mod))
                {
                    if (value == "on")
                    {
                        sender.SendMessage("Jukebox mode is already enabled.");
                    }
                    else if (value == "off")
                    {
                        sender.SendMessage("Jukebox mode disabled.");
                        m_lastMessage.Stop();
                        m_enabled = false;
                    }
                    else
                    {
                        SendMessage(sender);
                    }
                }
                else
                {
                    SendMessage(sender);
                }
            }
        }

        private void SendMessage(WinterBot bot)
        {
            m_lastMessage.Restart();
            bot.SendMessage(m_message);
        }
    }
}
