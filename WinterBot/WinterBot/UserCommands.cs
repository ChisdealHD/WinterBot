using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter
{
    class UserCommand
    {
        public AccessLevel AccessRequired { get; set; }
        string m_cmd;

        public string Command
        {
            get
            {
                return m_cmd;
            }
            
            set
            {
                value = value.ToLower();
                m_cmd = value;
            }
        }

        public string Value { get; set; }

        public UserCommand()
        {
        }


        public UserCommand(string req, string cmd, string value)
        {
            AccessLevel required;
            if (Enum.TryParse<AccessLevel>(req, true, out required))
                AccessRequired = required;
            else
                AccessRequired = AccessLevel.Mod;

            Command = cmd;
            Value = value;
        }

        public string Serialize()
        {
            return string.Format("{0} {1} {2}", AccessRequired, Command, Value);
        }

        public static UserCommand Deserialize(string line)
        {
            string[] values = line.Split(new char[] { ' ' }, 3);

            if (values.Length != 3)
                return null;

            return new UserCommand(values[0], values[1], values[2]);
        }

        public override string ToString()
        {
            return Command;
        }
    }


    class UserCommandTable : SavableDictionary<string, UserCommand>
    {
        public UserCommandTable(WinterBot bot)
            :base(bot, "commands")
        {
        }

        protected override IEnumerable<Tuple<string, UserCommand>> Deserialize(IEnumerable<string> lines)
        {
            foreach (var line in lines)
            {
                var cmd = UserCommand.Deserialize(line);
                if (cmd != null)
                    yield return new Tuple<string, UserCommand>(cmd.Command.ToLower(), cmd);
            }
        }

        protected override IEnumerable<string> Serialize(IEnumerable<Tuple<string, UserCommand>> values)
        {
            foreach (var value in values)
                yield return value.Item2.Serialize();
        }
    }


    public class UserCommands
    {
        string m_addCommandUsage = "Usage:  !addcommand -ul=[user|regular|sub|mod] !command [text]";
        string m_removeCommandUsage = "Usage:  !removecommand !command";
        string m_stream, m_dataDirectory;
        UserCommandTable m_commands;
        DateTime m_lastMessage = DateTime.Now;
        DateTime m_lastCommand = DateTime.Now;
        ChatOptions m_options;
        private WinterBot m_bot;
        
        public UserCommands(WinterBot bot)
        {
            m_bot = bot;
            var options = bot.Options;
            m_options = options.ChatOptions;
            m_stream = options.Channel;
            m_dataDirectory = bot.Options.DataDirectory;
            m_commands = new UserCommandTable(bot);

            bot.UnknownCommandReceived += UnknownCommandReceived;
        }

        [BotCommand(AccessLevel.Normal, "commands", "listcommands")]
        public void ListCommands(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!m_options.UserCommandsEnabled)
                return;

            int delay = m_options.UserCommandDelay;
            //if (m_lastMessage.Elapsed().TotalSeconds < delay || m_lastCommand.Elapsed().TotalSeconds < delay)
            //    return;

            Args args = value.ParseArguments(m_bot);
            AccessLevel level = args.GetAccessFlag("ul", user.Access);

            string part;
            switch (level)
            {
                case AccessLevel.Streamer:
                    part = "streamer";
                    break;

                case AccessLevel.Mod:
                    part = "moderators";
                    break;

                case AccessLevel.Normal:
                    part = "anyone";
                    break;

                case AccessLevel.Regular:
                    part = "regulars";
                    break;

                case AccessLevel.Subscriber:
                    part = "subscribers";
                    break;

                default:
                    return;
            }

            string[] cmds = (from c in m_commands.Values where c.AccessRequired <= level orderby c.Command select c.Command).ToArray();

            if (cmds.Length == 0)
                sender.SendResponse("No commands available.", part);
            else
                sender.SendResponse("Commands {0} can use: {1}", part, string.Join(", ", cmds));
        }
        
        [BotCommand(AccessLevel.Mod, "remcom", "removecommand", "delcom", "delcommand")]
        public void RemoveCommand(WinterBot sender, TwitchUser user, string c, string a)
        {
            if (!m_options.UserCommandsEnabled)
                return;

            Args args = a.ParseArguments(m_bot);
            string cmd = args.GetOneWord();
            if (cmd == null)
            {
                sender.SendResponse(m_removeCommandUsage);
                return;
            }

            if (cmd[0] == '!')
                cmd = cmd.Substring(1);

            cmd = cmd.ToLower();
            if (m_commands.ContainsKey(cmd))
                m_commands.Remove(cmd);
            else
                sender.SendResponse(string.Format("Command {0} not found.", cmd));
        }


        [BotCommand(AccessLevel.Mod, "addcom", "addcommand")]
        public void AddCommand(WinterBot sender, TwitchUser user, string c, string v)
        {
            if (!m_options.UserCommandsEnabled)
                return;

            Args args = v.ParseArguments(m_bot);

            AccessLevel level = args.GetAccessFlag("ul", AccessLevel.Mod);
            string cmdName = args.GetOneWord();
            string cmdText = args.GetString();

            if (string.IsNullOrWhiteSpace(cmdName) || string.IsNullOrWhiteSpace(cmdText) || args.Error != null)
            {
                sender.SendResponse(m_addCommandUsage);
                return;
            }
            
            if (cmdName[0] != '!')
            {
                sender.SendResponse(string.Format("User commands must start with a '!'. {0}", m_addCommandUsage));
                return;
            }
            else
            {
                cmdName = cmdName.Substring(1);
            }

            if (cmdText[0] == '.' || cmdText[0] == '/')
            {
                sender.SendResponse(string.Format("Cannot create a command which starts with a '{0}'.", cmdText[0]));
                return;
            }

            UserCommand userCommand = new UserCommand();
            userCommand.Value = cmdText;
            userCommand.AccessRequired = level;
            userCommand.Command = cmdName;

            cmdName = cmdName.ToLower();
            bool exists = m_commands.ContainsKey(cmdName);
            m_commands[cmdName] = userCommand;

            if (exists)
                sender.SendResponse(string.Format("Updated command: !{0}.", cmdName));
            else
                sender.SendResponse(string.Format("Successfully added command: !{0}.", cmdName));
        }

        void UnknownCommandReceived(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (!m_options.UserCommandsEnabled)
                return;

            cmd = cmd.ToLower();
            UserCommand command;

            if (m_commands.TryGetValue(cmd, out command))
            {
                if (sender.CanUseCommand(user, command.AccessRequired))
                {
                    // Keep user commands from spamming chat, only one once every 20 seconds (unless you are a mod).
                    if (m_lastMessage.Elapsed().TotalSeconds >= m_options.UserCommandDelay || sender.CanUseCommand(user, AccessLevel.Mod))
                    {
                        sender.SendResponse(command.Value);
                        m_lastMessage = DateTime.Now;
                    }
                }
            }
        }
    }
}
