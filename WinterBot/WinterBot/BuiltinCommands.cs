using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinterBot
{
    public class BuiltinCommands
    {
        public BuiltinCommands(WinterBot sender)
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
            value = value.Trim().ToLower();

            var userData = sender.UserData;
            if (!TwitchData.IsValidUserName(value))
            {
                sender.WriteDiagnostic(DiagnosticLevel.Notify, "{0}: Invalid username '{1}.", cmd, value);
                return;
            }

            if (regular)
            {
                sender.AddRegular(value);
                sender.SendMessage("{0} added to regular list.", value);
            }
            else
            {
                sender.RemoveRegular(value);
                sender.SendMessage("{0} removed from regular list.", value);
            }
        }
    }
}
