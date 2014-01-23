using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinterBot
{
    enum EventKind
    {
        Default,
        Timeout,
        Message,
        Subscribe
    }

    class BaseEvent
    {
        public TwitchUser User { get; set; }

        public EventKind Kind { get; set; }

        public DateTime Timestamp { get; set; }

        public BaseEvent(TwitchUser user, EventKind kind)
        {
            Timestamp = DateTime.Now;
            User = user;
            Kind = kind;
        }
    }

    class TimeoutEvent : BaseEvent
    {
        public TimeoutEvent(TwitchUser user)
            : base(user, EventKind.Timeout)
        {
        }
    }

    class MessageEvent : BaseEvent
    {
        public string Text { get; set; }

        public MessageEvent(TwitchUser user, string text)
            : base(user, EventKind.Message)
        {
            Text = text;
        }
    }

    class UserSubscribeEvent : BaseEvent
    {
        public UserSubscribeEvent(TwitchUser user)
            : base(user, EventKind.Subscribe)
        {
        }
    }


}
