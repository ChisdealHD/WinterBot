using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;


namespace WinterBot
{
    public enum AccessLevel
    {
        Normal,
        Regular,
        Subscriber,
        Mod
    }

    [Serializable]
    public abstract class WinterBotCommand
    {
        public IEnumerable<string> Commands { get; set; }

        public AccessLevel AccessRequired { get; set; }

        public WinterBotCommand(AccessLevel access)
        {
            AccessRequired = access;
        }

        abstract public bool Execute(TwitchUser user, string cmd, string value, TwitchData data, TwitchClient twitch, WinterBotController controller);

        public bool CanUseCommand(TwitchUser user)
        {
            switch (AccessRequired)
            {
                case AccessLevel.Mod:
                    return user.IsModerator;

                case AccessLevel.Subscriber:
                    return user.IsSubscriber || user.IsModerator;

                case AccessLevel.Regular:
                    return user.IsSubscriber || user.IsModerator || user.IsRegular;
            }

            return true;
        }
    }

    [Serializable]
    public class RegularCommand : WinterBotCommand
    {
        public RegularCommand()
            : base(AccessLevel.Mod)
        {
            Commands = new string[] { "regular", "addregular", "delregular", "deleteregular", "remregular", "removeregular" };
        }

        public override bool Execute(TwitchUser user, string cmd, string value, TwitchData data, TwitchClient twitch, WinterBotController controller)
        {
            bool add = false;
            switch (cmd)
            {
                case "regular":
                case "addregular":
                    add = true;
                    break;

                case "delregular":
                case "deleteregular":
                case "remregular":
                case "removeregular":
                    break;

                default:
                    return false;
            }

            value = value.Trim().ToLower();
            TwitchUser target = null;
            if (!data.IsValidUserName(value) || (target = data.GetUser(value)) == null)
            {
                twitch.SendMessage(string.Format("Invalid user {0}.", user.Name));
                return false;
            }

            if (add)
            {
                data.SetRegular(user, true);
                twitch.SendMessage(string.Format("{0} added to regulars list.", value));

                return true;
            }
            else
            {
                data.SetRegular(user, false);
                twitch.SendMessage(string.Format("{0} removed from regulars list.", value));

                return true;
            }
        }
    }


    [Serializable]
    public class PermitCommand : WinterBotCommand
    {
        public PermitCommand()
            : base(AccessLevel.Mod)
        {
            Commands = new string[] { "permit" };
        }

        public override bool Execute(TwitchUser user, string cmd, string value, TwitchData data, TwitchClient twitch, WinterBotController controller)
        {
            if (!CanUseCommand(user))
                return false;

            value = value.Trim().ToLower();
            TwitchUser target = null;
            if (!data.IsValidUserName(value) || ((target = data.GetUser(value)) == null))
            {
                twitch.SendMessage(string.Format("Invalid user '{0}'.  Usage:  !permit [username]", value));
                return false;
            }

            target.PermitLinkPost(1);
            twitch.SendMessage(string.Format("{0} -> {1} has been granted permission to post a single link.", user.Name, target.Name));
            return true;
        }
    }


    [Serializable]
    class UserCommandController : WinterBotCommand
    {
        public UserCommandController()
            : base(AccessLevel.Mod)
        {
            Commands = new string[] { "addcom", "addcommand", "removecommand", "delcommand", "remcommand", "listcommands", "commands" };
        }

        public override bool Execute(TwitchUser user, string cmd, string value, TwitchData data, TwitchClient twitch, WinterBotController controller)
        {
            if (!CanUseCommand(user))
                return false;

            cmd = cmd.ToLower();
            switch (cmd)
            {
                case "addcom":
                case "addcommand":
                    return OnAddCommand(user, value, data, twitch, controller);

                case "delcommand":
                case "remcommand":
                case "removecommand":
                    return OnRemoveCommand(user, value, data, twitch, controller);
                    
                case "commands":
                case "listcommands":
                    return OnListCommands(user, value, data, twitch, controller);
            }

            return false;
        }

        private bool OnAddCommand(TwitchUser user, string value, TwitchData data, TwitchClient twitch, WinterBotController controller)
        {
            string cmdValue = value.Trim();
            if (cmdValue.Length < 2)
            {
                WriteOutput(twitch, m_addCommandUsage);
                return false;
            }

            string[] split = cmdValue.ToLower().Split(new char[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (split.Length < 2)
            {
                WriteOutput(twitch, m_addCommandUsage);
                return false;
            }

            AccessLevel level = AccessLevel.Mod;

            int cmd = 0;
            int len = Math.Min(4, split[0].Length);
            if (split[0].Substring(0, len) == "-ul=")
            {
                cmd = 1;
                var access = split[0].Substring(4);

                switch (access)
                {
                    case "user":
                        level = AccessLevel.Normal;
                        break;

                    case "sub":
                    case "subscriber":
                        level = AccessLevel.Subscriber;
                        break;

                    case "regular":
                    case "reg":
                        level = AccessLevel.Regular;
                        break;

                    case "mod":
                    case "moderator":
                        level = AccessLevel.Mod;
                        break;

                    default:
                        WriteOutput(twitch, "Invalid user level {0}. {1}", access, m_addCommandUsage);
                        return false;
                }
            }


            if (split[cmd].Length < 2 || split[cmd][0] != '!')
            {
                WriteOutput(twitch, "User commands must start with a '!'. {0}", m_addCommandUsage);
                return false;
            }

            string cmdName = split[cmd].Substring(1);
            string cmdText = value.Substring(cmdValue.IndexOf(cmdName) + cmdName.Length).Trim();

            bool exists = controller.GetCommand(cmdName) != null;
            if (cmdText[0] == '.' || cmdText[0] == '/')
            {
                WriteOutput(twitch, "Cannot create a command which starts with a '{0}'.", cmdText[0]);
                return false;
            }

            var existingCmd = controller.GetCommand(cmdName);
            if (existingCmd != null && !(existingCmd is UserCommand))
            {
                WriteOutput(twitch, "You cannot modify built in commands.");
                return false;
            }

            UserCommand userCommand = new UserCommand(cmdText);
            userCommand.AccessRequired = level;
            userCommand.Commands = new string[] { cmdName };
            controller.AddCommand(userCommand);

            if (exists)
                WriteOutput(twitch, "Updated command: !{0}.", cmdName);
            else
                WriteOutput(twitch, "Successfully added command: !{0}.", cmdName);

            return true;
        }

        private bool OnListCommands(TwitchUser user, string value, TwitchData data, TwitchClient twitch, WinterBotController controller)
        {
            StringBuilder sb = new StringBuilder();

            var cmds = EnumerateUserCommands(controller);
            List<UserCommand> userCmds = new List<UserCommand>(from cmd in cmds
                                                           where cmd.AccessRequired == AccessLevel.Normal
                                                           select cmd);
            
            List<UserCommand> sub = new List<UserCommand>(from cmd in cmds
                                                           where cmd.AccessRequired == AccessLevel.Subscriber || cmd.AccessRequired == AccessLevel.Regular
                                                           select cmd);

            List<UserCommand> mod = new List<UserCommand>(from cmd in cmds
                                                           where cmd.AccessRequired == AccessLevel.Mod
                                                           select cmd);

            AddCmds(sb, "User", userCmds);
            AddCmds(sb, "Subscriber", sub);
            AddCmds(sb, "Mod", mod);

            WriteOutput(twitch, sb.ToString());
            return true;
        }

        private static void AddCmds(StringBuilder sb, string type, List<UserCommand> user)
        {
            if (user.Count > 0)
                sb.AppendFormat("{0} cmds: {1}. ", type, string.Join(", ", EnumCommands(user)));
        }

        private static IEnumerable<string> EnumCommands(List<UserCommand> cmds)
        {
            foreach (var cmd in cmds)
                foreach (string val in cmd.Commands)
                    yield return val;
        }

        private static IEnumerable<UserCommand> EnumerateUserCommands(WinterBotController controller)
        {
            var cmds = from cmd in controller.Commands
                       where cmd is UserCommand
                       select (UserCommand)cmd;
            return cmds;
        }

        IEnumerable<string> EnumerateAllCommands(IEnumerable<WinterBotCommand> cmds)
        {
            foreach (var cmd in cmds)
                if (cmd is UserCommand)
                    foreach (string cmdName in cmd.Commands)
                        yield return cmdName;
        }

        private bool OnRemoveCommand(TwitchUser user, string value, TwitchData data, TwitchClient twitch, WinterBotController controller)
        {
            value = value.Trim().ToLower();
            if (value.Length == 0)
            {
                WriteOutput(twitch, m_removeCommandUsage);
                return false;
            }

            if (value[0] != '!')
                value = "!" + value;

            var cmd = controller.GetCommand(value);
            if (cmd == null)
            {
                WriteOutput(twitch, "Command {0} not found.", value);
                return false;
            }

            controller.RemoveCommand(value);
            return true;
        }


        private void WriteOutput(TwitchClient client, string fmt, params object[] values)
        {
            string value = string.Format(fmt, values);
            client.SendMessage(value);
        }

        string m_addCommandUsage = "Usage:  !addcommand -ul=[user|regular|sub|mod] !command [text]";
        string m_removeCommandUsage = "Usage:  !removecommand !command";
    }



    [Serializable]
    class TextCommand : WinterBotCommand
    {
        string m_output;

        public TextCommand()
            : base(AccessLevel.Regular)
        {
        }

        public TextCommand(string output)
            : base(AccessLevel.Regular)
        {
            m_output = output;
        }

        public override bool Execute(TwitchUser user, string cmd, string value, TwitchData data, TwitchClient twitch, WinterBotController controller)
        {
            if (!CanUseCommand(user))
                return false;

            twitch.SendMessage(string.Format(m_output, user.Name, value));
            return true;
        }
    }

    [Serializable]
    class UserCommand : TextCommand
    {
        public UserCommand()
        {
        }

        public UserCommand(string output)
            : base(output)
        {
        }
    }
}