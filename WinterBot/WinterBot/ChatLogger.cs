using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Winter
{
    class ChatLog : SaveQueue<ChatEvent>
    {
        public ChatLog(WinterBot bot)
            : base(bot)
        {
        }

        public override string Filename
        {
            get
            {
                DateTime now = DateTime.Now;
                string filename = string.Format("{0}_{1:00}_{2:00}_{3:00}.txt", Bot.Channel, now.Year, now.Month, now.Day);
                return Path.Combine(Bot.Options.DataDirectory, "logs", filename);
            }
        }

        protected override IEnumerable<string> Serialize(IEnumerable<ChatEvent> data)
        {
            foreach (var item in data)
                yield return item.ToString();
        }

        public static void Init(WinterBot bot)
        {
            var options = bot.Options;
            if (!options.ChatOptions.SaveLog)
                return;

            var log = new ChatLog(bot);
            bot.MessageReceived += delegate(WinterBot sender, TwitchUser user, string text) { log.Add(new ChatMessage(user, text)); };
            bot.ChatClear += delegate(WinterBot sender, TwitchUser user) { log.Add(new ChatClearEvent(user)); };
            bot.UserSubscribed += delegate(WinterBot sender, TwitchUser user) { log.Add(new ChatSubscribeEvent(user)); };
            bot.UserBanned += delegate(WinterBot sender, TwitchUser user) { log.Add(new ChatBanEvent(user)); };
            bot.UserTimedOut += delegate(WinterBot sender, TwitchUser user, int duration) { log.Add(new ChatTimeout(user, duration)); };
            bot.ModeratorAdded += delegate(WinterBot sender, TwitchUser user) { log.Add(new ChatModEvent(user, true)); };
            bot.ModeratorRemoved += delegate(WinterBot sender, TwitchUser user) { log.Add(new ChatModEvent(user, false)); };
        }
    }

    abstract class ChatEvent
    {
        public DateTime Timestamp { get; set; }
        public string User { get; set; }

        public ChatEvent(TwitchUser user)
        {
            Timestamp = DateTime.Now;
            User = user.Name;
        }

        public override string ToString()
        {
            return string.Format("[{0}] {1}", Timestamp, User);
        }
    }

    class ChatMessage : ChatEvent
    {
        public string Message { get; set; }

        public ChatMessage(TwitchUser user, string message)
            : base(user)
        {
            Message = message;
        }

        public override string ToString()
        {
            return string.Format("{0}: {1}", base.ToString(), Message);
        }
    }

    class ChatSubscribeEvent : ChatEvent
    {
        public ChatSubscribeEvent(TwitchUser user)
            : base(user)
        {
        }

        public override string ToString()
        {
            return base.ToString() + " subscribed!";
        }
    }

    class ChatClearEvent : ChatEvent
    {
        public ChatClearEvent(TwitchUser user)
            : base(user)
        {
        }

        public override string ToString()
        {
            return base.ToString() + " timed out.";
        }
    }

    class ChatBanEvent : ChatEvent
    {
        public ChatBanEvent(TwitchUser user)
            : base(user)
        {
        }

        public override string ToString()
        {
            return base.ToString() + " banned by this bot.";
        }
    }

    class ChatModEvent : ChatEvent
    {
        private bool m_added;

        public ChatModEvent(TwitchUser user, bool added)
            : base(user)
        {
            m_added = added;
        }

        public override string ToString()
        {
            return base.ToString() + string.Format(" {0} chat.", m_added ? "joined" : "left");
        }
    }

    class ChatTimeout : ChatEvent
    {
        public int Duration { get; set; }
        public ChatTimeout(TwitchUser user, int duration)
            : base(user)
        {
            Duration = duration;
        }

        public override string ToString()
        {
            return string.Format("{0} timed out for {1} seconds by this bot.", base.ToString(), Duration);
        }
    }
}
