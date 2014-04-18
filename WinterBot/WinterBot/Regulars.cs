using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter
{
    public class Regulars
    {
        public Regulars(WinterBot sender)
        {
        }

        [BotCommand(AccessLevel.Mod, "addreg", "addregular")]
        public void AddRegular(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            SetRegular(sender, cmd, value, true);
        }

        [BotCommand(AccessLevel.Mod, "delreg", "delregular", "remreg", "remregular")]
        public void RemoveRegular(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            SetRegular(sender, cmd, value, false);
        }

        private void SetRegular(WinterBot sender, string cmd, string value, bool regular)
        {
            value = value.Trim();
            if (!TwitchUsers.IsValidUserName(value))
            {
                sender.WriteDiagnostic(DiagnosticFacility.UserError, "{0}: Invalid username '{1}.", cmd, value);
                return;
            }

            TwitchUser target = sender.Users.GetUser(value);
            target.IsRegular = regular;
            sender.SendResponse(Importance.Med, "{0} {1} the regular list.", target.Name, regular ? "added to " : "removed from");
        }
    }
}
