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
        string m_message = " The jukebox is OPEN. Want your song played? Donate $2.50 (http://bit.ly/1gBKIqa) and include a Youtube link to the song in the Message field. Please keep it less than 6 minutes.";

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
                        sender.SendResponse("Disabling jukebox mode.");
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
                    sender.SendResponse("The jukebox is CLOSED. No additional requests are being accepted.");
                    return;
                }

                value = value.Trim().ToLower();

                if (value == "on")
                {
                    m_enabled = true;
                    m_lastMessage = DateTime.Now;
                    sender.SendResponse("Jukebox activated.  Use '!JukeboxMode off' to deactivate.");
                }
                else if (value == "off")
                {
                    sender.SendResponse("Jukebox mode is off.");
                }
                else
                {
                    sender.SendResponse("Usage: '!jukebox on' and '!jukebox off'.  Mod only.");
                }
            }
            else
            {
                if (sender.CanUseCommand(user, AccessLevel.Mod))
                {
                    if (value == "on")
                    {
                        sender.SendResponse("Jukebox mode is already enabled.");
                    }
                    else if (value == "off")
                    {
                        sender.SendResponse("The jukebox is shutting down for the night. Please hold your song requests for next time.");
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
            bot.SendResponse(m_message);
        }
    }
}
