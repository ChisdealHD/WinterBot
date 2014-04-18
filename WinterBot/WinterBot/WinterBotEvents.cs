using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter.BotEvents
{
    enum EventType
    {
        ViewerCount,
        Action,
        Message,
        Subscribe,
        Mod,
        Clear,
        StreamStatus,
        Follow,
    }

    class WinterBotEvent
    {
        public EventType Event { get; private set; }

        public WinterBotEvent(EventType evt)
        {
            Event = evt;
        }
    }

    class ViewerCountEvent : WinterBotEvent
    {
        public ViewerCountEvent(int viewers)
            : base(EventType.ViewerCount)
        {
            Viewers = viewers;
        }

        public int Viewers { get; set; }
    }

    class ActionEvent : WinterBotEvent
    {
        public TwitchUser User;
        public string Text;

        public ActionEvent(TwitchUser user, string text)
            : base(EventType.Action)
        {
            User = user;
            Text = text;
        }
    }
    class MessageEvent : WinterBotEvent
    {
        public TwitchUser User;
        public string Text;

        public MessageEvent(TwitchUser user, string text)
            : base(EventType.Message)
        {
            User = user;
            Text = text;
        }
    }

    class ClearEvent : WinterBotEvent
    {
        public TwitchUser User;

        public ClearEvent(TwitchUser user)
            : base(EventType.Clear)
        {
            User = user;
        }
    }

    class SubscribeEvent : WinterBotEvent
    {
        public TwitchUser User;

        public SubscribeEvent(TwitchUser user)
            : base(EventType.Subscribe)
        {
            User = user;
        }
    }
    class FollowEvent : WinterBotEvent
    {
        public TwitchUser User;

        public FollowEvent(TwitchUser user)
            : base(EventType.Follow)
        {
            User = user;
        }
    }

    class ModEvent : WinterBotEvent
    {
        public TwitchUser User;
        public bool Mod;

        public ModEvent(TwitchUser user, bool mod)
            : base(EventType.Mod)
        {
            User = user;
            Mod = mod;
        }
    }

    class StreamStatusEvent : WinterBotEvent
    {
        public bool Online;

        public StreamStatusEvent(bool online)
            : base(EventType.StreamStatus)
        {
            Online = online;
        }
    }
}
