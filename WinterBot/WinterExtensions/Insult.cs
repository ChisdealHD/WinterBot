using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Winter;

namespace WinterExtensions
{
    class Insult
    {
        Random m_random = new Random();
        DateTime m_last = DateTime.Now;

        public Insult(WinterBot bot)
        {
            m_last = DateTime.Now.AddHours(-1);
            bot.MessageReceived += bot_MessageReceived;
        }

        void bot_MessageReceived(WinterBot sender, TwitchUser user, string text)
        {
            if (!user.Name.Equals("frostysc", StringComparison.CurrentCultureIgnoreCase))
                return;

            if (m_random.Next(200) != 7)
                return;

            if (m_last.Elapsed().Minutes >= 5)
                sender.SendMessage("{0}, {1}", user.Name, m_insults[m_random.Next(m_insults.Length)]);

            m_last = DateTime.Now;
        }

        string[] m_insults = new string[]{
            "it's kinda sad watching you attempt to fit your entire vocabulary into a sentence.",
            "everyone who ever loved you was wrong.",
            "your parents wouldn't happen to be siblings, would they?",
            "I hope your day is as pleasant as you are.",
            "you have a face for radio.",
            "I hope you realize everyone's just putting up with you.",
            "everyone who has ever loved you was wrong.",
            "I don't have the time or crayons to explain it to you.",
            "you're about as useful as Anne Frank's drum kit.",
            "I'm not mad, I'm just disappointed.",
            "you filthy casual."
        };
        
        [BotCommand(AccessLevel.Normal, "insult")]
        public void InsultUser(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            value = value.Trim().ToLower();
            if (TwitchUsers.IsValidUserName(value))
                user = sender.Users.GetUser(value);

            if (m_last.Elapsed().Minutes >= 1)
                sender.SendMessage("{0}, {1}", user.Name, m_insults[m_random.Next(m_insults.Length)]);

            m_last = DateTime.Now;
        }


    }
}
