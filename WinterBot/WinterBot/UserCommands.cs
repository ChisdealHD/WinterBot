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

        public UserCommand(string line)
        {
            string[] values = line.Split(new char[] { ' ' }, 3);

            AccessLevel required;
            if (Enum.TryParse<AccessLevel>(values[0], true, out required))
                AccessRequired = required;
            else
                AccessRequired = AccessLevel.Mod;

            Command = values[1];
            Value = values[2];
        }

        public override string ToString()
        {
            return Command;
        }
    }

    public class UserCommands
    {
        string m_addCommandUsage = "Usage:  !addcommand -ul=[user|regular|sub|mod] !command [text]";
        string m_removeCommandUsage = "Usage:  !removecommand !command";
        string m_stream, m_dataDirectory;
        Dictionary<string, UserCommand> m_commands = new Dictionary<string, UserCommand>();
        DateTime m_lastMessage = DateTime.Now;

        public UserCommands(WinterBot bot)
        {
            m_stream = bot.Options.Channel;
            m_dataDirectory = bot.Options.DataDirectory;
            bot.UnknownCommandReceived += UnknownCommandReceived;

            LoadCommands();
        }

        [BotCommand(AccessLevel.Normal, "commands", "listcommands")]
        public void ListCommands(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            AccessLevel level;
            string part = null;
            if (user.IsModerator)
            {
                part = "moderators";
                level = AccessLevel.Mod;
            }
            else if (user.IsSubscriber)
            {
                part = "subscribers";
                level = AccessLevel.Subscriber;
            }
            else if (sender.IsRegular(user))
            {
                part = "regulars";
                level = AccessLevel.Regular;
            }
            else
            {
                part = "anyone";
                level = AccessLevel.Normal;
            }

            sender.SendMessage("Commands {0} can use: {1}", part, string.Join(", ", (from c in m_commands.Values where  c.AccessRequired <= level orderby c.AccessRequired, c.Command select c.Command)));
        }
        
        [BotCommand(AccessLevel.Mod, "remcom", "removecommand", "delcom", "delcommand")]
        public void RemoveCommand(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            value = value.Trim().ToLower();
            if (value.Length == 0)
            {
                sender.SendMessage(m_removeCommandUsage);
                return;
            }

            if (value[0] == '!')
                value = value.Substring(1);

            value = value.ToLower();
            if (m_commands.ContainsKey(value))
            {
                m_commands.Remove(value);
                SaveCommands();
            }
            else
            {
                sender.SendMessage(string.Format("Command {0} not found.", value));
            }
        }


        [BotCommand(AccessLevel.Mod, "addcom", "addcommand")]
        public void AddCommand(WinterBot sender, TwitchUser user, string commandText, string value)
        {
            string cmdValue = value.Trim();
            if (cmdValue.Length < 2)
            {
                sender.SendMessage(m_addCommandUsage);
                return;
            }

            string[] split = cmdValue.ToLower().Split(new char[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
            if (split.Length < 2)
            {
                sender.SendMessage(m_addCommandUsage);
                return;
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
                        sender.SendMessage(string.Format("Invalid user level {0}. {1}", access, m_addCommandUsage));
                        return;
                }
            }


            if (split[cmd].Length < 2 || split[cmd][0] != '!')
            {
                sender.SendMessage(string.Format("User commands must start with a '!'. {0}", m_addCommandUsage));
                return;
            }

            string cmdName = split[cmd].Substring(1);
            string cmdText = value.Substring(cmdValue.IndexOf(cmdName) + cmdName.Length).Trim();

            if (cmdText[0] == '.' || cmdText[0] == '/')
            {
                sender.SendMessage(string.Format("Cannot create a command which starts with a '{0}'.", cmdText[0]));
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
                sender.SendMessage(string.Format("Updated command: !{0}.", cmdName));
            else
                sender.SendMessage(string.Format("Successfully added command: !{0}.", cmdName));

            SaveCommands();
        }

        void UnknownCommandReceived(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            cmd = cmd.ToLower();
            UserCommand command;

            if (m_commands.TryGetValue(cmd, out command))
            {
                if (sender.CanUseCommand(user, command.AccessRequired))
                {
                    // Keep user commands from spamming chat, only one once every 20 seconds (unless you are a mod).
                    if (m_lastMessage.Elapsed().TotalSeconds >= 10 || sender.CanUseCommand(user, AccessLevel.Mod))
                    {
                        sender.SendMessage(command.Value);
                        m_lastMessage = DateTime.Now;
                    }
                }
            }
        }

        private void LoadCommands()
        {
            string filename = GetFileName();
            if (!File.Exists(filename))
                return;

            string[] lines = File.ReadAllLines(filename);

            m_commands.Clear();
            foreach (var line in lines)
            {
                UserCommand cmd = new UserCommand(line);
                m_commands[cmd.Command.ToLower()] = cmd;
            }
        }

        private void SaveCommands()
        {
            File.WriteAllLines(GetFileName(), from cmd in m_commands.Values orderby cmd.AccessRequired, cmd.Command select string.Format("{0} {1} {2}", cmd.AccessRequired, cmd.Command, cmd.Value));
        }

        private string GetFileName()
        {
            return Path.Combine(m_dataDirectory, m_stream + "_commands.txt");
        }
    }
}
