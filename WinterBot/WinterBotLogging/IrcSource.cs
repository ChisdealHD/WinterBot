using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinterBotLogging
{
    [EventSource(Name = "WinterBot-Irc")]
    public class IrcSource : EventSource
    {
        static public IrcSource Log = new IrcSource();

        [Event(1, Keywords = Keywords.Communication)]
        public void MessageReceived(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                WriteEvent(1, message.TrimEnd('\r', '\n'));
        }

        [Event(2, Keywords=Keywords.Communication)]
        public void MessageQueued(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && ! message.StartsWith("PASS"))
                WriteEvent(2, message.TrimEnd('\r', '\n'));
        }

        [Event(3, Keywords = Keywords.Communication)]
        public void MessageSent(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && !message.StartsWith("PASS"))
                WriteEvent(3, message.TrimEnd('\r', '\n'));
        }

        [Event(4, Keywords = Keywords.Communication)]
        public void FloodPrevented(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                WriteEvent(4, message);
        }

        public class Keywords
        {
            public const EventKeywords Communication = (EventKeywords)0x0001;
        }
    }
}
