using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinterBotLogging
{
    [EventSource(Name = "WinterBot-Twitch")]
    public class TwitchSource : EventSource
    {
        public static TwitchSource Log = new TwitchSource();

        [Event(1, Keywords = Keywords.Control)]
        public void Connected(string channel)
        {
            WriteEvent(1, channel);
        }

        [Event(2, Keywords = Keywords.Control)]
        public void SentPing()
        {
            WriteEvent(2);
        }

        [Event(3, Keywords = Keywords.Control)]
        public void ReceivedPong()
        {
            WriteEvent(3);
        }

        [Event(4, Keywords = Keywords.Control)]
        public void Quit()
        {
            WriteEvent(4);
        }

        [Event(5, Keywords = Keywords.Control)]
        public void SoftMessageDrop(string msg, int imp, int remaining)
        {
            WriteEvent(5, msg, imp, remaining);
        }

        [Event(6, Keywords = Keywords.Messages)]
        public void SentMessage(string text)
        {
            WriteEvent(6, text);
        }

        [Event(7, Keywords = Keywords.Messages)]
        public void TimeoutUser(string user, int duration)
        {
            WriteEvent(7, user, duration);
        }

        [Event(8, Keywords = Keywords.Messages)]
        public void BanUser(string user)
        {
            WriteEvent(8, user);
        }
        
        [Event(9, Keywords = Keywords.Messages)]
        public void ReceivedMessage(string name, string text)
        {
            WriteEvent(9, name, text);
        }
        
        [Event(10, Keywords = Keywords.Messages)]
        public void ReceivedPrivateMessage(string name, string text)
        {
            WriteEvent(10, name, text);
        }

        public class Keywords
        {
            public const EventKeywords Control = (EventKeywords)0x0001;
            public const EventKeywords Flood = (EventKeywords)0x0002;
            public const EventKeywords Messages = (EventKeywords)0x0004;
        }
    }
}
