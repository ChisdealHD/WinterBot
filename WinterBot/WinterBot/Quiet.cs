using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter
{
    class Quiet
    {
        [BotCommand(AccessLevel.Mod, "silent", "silentmode")]
        public void SilentMode(WinterBot bot, TwitchUser user, string cmd, string value)
        {
            bot.Silent = !bot.Silent;
            bot.Send(MessageType.Unconditional, "Silent mode now {0}.", bot.Silent ? "enabled" : "disabled");
        }

        [BotCommand(AccessLevel.Mod, "quiet", "quietmode")]
        public void QuietMode(WinterBot bot, TwitchUser user, string cmd, string value)
        {
            bot.Quiet = !bot.Quiet;
            bot.Send(MessageType.Unconditional, "Quiet mode now {0}.", bot.Quiet ? "enabled" : "disabled");
        }
    }
}
