using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinterBot
{
    public enum EventKind
    {
        Default,
        Timeout,
        Message,
        InformModerator,
        InformSubscriber,
        Subscribe
    }

    public class BaseEvent
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

    public class ModeratorNotifyEvent : BaseEvent
    {
        public ModeratorNotifyEvent(TwitchUser user)
            : base(user, EventKind.InformModerator)
        {
        }
    }

    public class TimeoutEvent : BaseEvent
    {
        public TimeoutEvent(TwitchUser user)
            : base(user, EventKind.Timeout)
        {
        }
    }

    public class MessageEvent : BaseEvent
    {
        public string Text { get; set; }

        public MessageEvent(TwitchUser user, string text)
            : base(user, EventKind.Message)
        {
            Text = text;
        }
    }

    public class SubscriberNotifyEvent : BaseEvent
    {
        public SubscriberNotifyEvent(TwitchUser user)
            : base(user, EventKind.InformSubscriber)
        {
        }
    }

    public class UserSubscribeEvent : BaseEvent
    {
        public UserSubscribeEvent(TwitchUser user)
            : base(user, EventKind.Subscribe)
        {
        }
    }


}
