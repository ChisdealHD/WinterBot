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
                WriteEvent(1, message);
        }

        [Event(2, Keywords=Keywords.Communication)]
        public void MessageSent(string message)
        {
            if (!string.IsNullOrWhiteSpace(message) && ! message.StartsWith("PASS"))
                WriteEvent(2, message);
        }

        [Event(3, Keywords = Keywords.Communication)]
        public void FloodPrevented(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                WriteEvent(3, message);
        }

        public class Keywords
        {
            public const EventKeywords Communication = (EventKeywords)0x0001;
        }
    }
}
