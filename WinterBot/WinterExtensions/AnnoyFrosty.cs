using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Winter;

namespace WinterExtensions
{
    public class AnnoyFrosty
    {
        Random m_random = new Random();
        DateTime m_lastMessage = DateTime.Now.AddDays(-2);
        string[] m_messages = {
                                  "Frosty if you don't calm down we are going to have to put down newspaper on the floor.",
                                  "Frosty touches animals.  I have proof.",
                                  "Frosty touched me inappropriately.  I need an adult. :("
                              };

        public AnnoyFrosty(WinterBot bot)
        {
            bot.MessageReceived += bot_MessageReceived;
        }

        void bot_MessageReceived(WinterBot sender, TwitchUser user, string text)
        {
            if (!user.Name.Equals("frostysc", StringComparison.CurrentCultureIgnoreCase))
                return;

            if (m_lastMessage.Elapsed().TotalDays < 1)
                return;

            if (m_random.Next(0, 25) != 10)
                return;

            m_lastMessage = DateTime.Now;
            sender.SendMessage(m_messages[m_random.Next(0, m_messages.Length - 1)]);
        }
    }
}
