using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WinterBotLogging;

namespace Winter
{
    class Quiet
    {
        public Quiet(WinterBot bot)
        {
        }

        [BotCommand(AccessLevel.Mod, "silent", "silentmode")]
        public void SilentMode(WinterBot bot, TwitchUser user, string cmd, string value)
        {
            bot.Silent = !bot.Silent;
            bot.SendUnconditional("Silent mode now {0}.", bot.Silent ? "enabled" : "disabled");
        }

        [BotCommand(AccessLevel.Mod, "quiet", "quietmode")]
        public void QuietMode(WinterBot bot, TwitchUser user, string cmd, string value)
        {
            bot.Quiet = !bot.Quiet;
            bot.SendUnconditional("Quiet mode now {0}.", bot.Quiet ? "enabled" : "disabled");
        }

        [BotCommand(AccessLevel.Streamer, "kill")]
        public void Kill(WinterBot bot, TwitchUser user, string cmd, string value)
        {
            bot.WriteDiagnostic(DiagnosticFacility.Info, "Bot killed by streamer.");
            WinterBotSource.Log.Kill();

            bot.Shutdown();
        }
    }
}
