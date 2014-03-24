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
        bool m_streamDead = false;
        DateTime m_lastOffline = DateTime.Now;
        DateTime m_lastMessage = DateTime.Now;
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
                    if (!m_streamDead)
                    {
                        m_streamDead = true;
                        m_lastOffline = DateTime.Now;
                    }
                    else if (m_lastOffline.Elapsed().TotalMinutes >= 10)
                    {
                        m_enabled = false;
                        m_streamDead = false;
                        sender.SendMessage("Disabling jukebox mode.");
                    }
                }
                else
                {
                    m_streamDead = false;
                }
            }

            if (m_enabled && m_lastMessage.Elapsed().TotalMinutes >= 7)
                SendMessage(sender);
        }
        

        [BotCommand(AccessLevel.Normal, "jukebox", "jukeboxmode")]
        public void JukeBoxCommand(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!m_enabled)
            {
                if (!sender.CanUseCommand(user, AccessLevel.Mod))
                {
                    sender.SendMessage("Winter is not currently accepting donations for song requests.");
                    return;
                }

                value = value.Trim().ToLower();

                if (value == "on")
                {
                    m_enabled = true;
                    m_lastMessage = DateTime.Now;
                    sender.SendMessage("Jukebox mode enabled!  Use '!JukeboxMode off' to turn it off.");
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
                        sender.SendMessage("Winter jukebox time is done for the night!  No more donations for songs until next time.");
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
            m_lastMessage = DateTime.Now;
            bot.SendMessage(m_message);
        }
    }
}
