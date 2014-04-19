using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Winter;

namespace WinterExtensions
{

    class BetterCommands : AutoSave
    {
        const string m_addCommandUsage = "Usage:  !addcommand -ul=[user|regular|sub|mod] !command [text]";
        const string m_removeCommandUsage = "Usage:  !removecommand !command";

        WinterBot m_bot;
        WinterOptions m_options;
        volatile bool m_dirty;
        Dictionary<string, Command> m_commands = new Dictionary<string,Command>();
        HashSet<string> m_remove = new HashSet<string>();
        object m_sync = new object();
        DateTime m_lastCommandList = DateTime.Now;

        const int TimeSpan = 30;
        const int MaxMessages = 5;

        LinkedList<Tuple<string, DateTime>> m_sent = new LinkedList<Tuple<string, DateTime>>();

        public BetterCommands(WinterBot bot, WinterOptions options)
            : base(bot)
        {
            if (bot.Options.ChatOptions.UserCommandsEnabled)
                return;

            m_bot = bot;
            m_options = options;
            m_bot.UnknownCommandReceived += UnknownCommandReceived;

            HttpManager.Instance.GetAsync("api.php", "GETCMDS=1", Load);
        }

        [BotCommand(AccessLevel.Normal, "commands", "listcommands")]
        public void ListCommands(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            if (m_lastCommandList.Elapsed().TotalSeconds < 15)
                return;

            m_lastCommandList = DateTime.Now;
            sender.SendMessage("A full listing of user commands can be found here: " + HttpManager.Instance.GetUrl("commands.php"));
            m_sent.AddLast(new Tuple<string, DateTime>("commands", DateTime.Now));
        }

        [BotCommand(AccessLevel.Mod, "remcom", "removecommand", "delcom", "delcommand")]
        public void RemoveCommand(WinterBot sender, TwitchUser user, string c, string a)
        {
            Args args = a.ParseArguments(m_bot);
            string cmd = args.GetOneWord();
            if (cmd == null)
            {
                sender.SendResponse(Importance.Med, m_removeCommandUsage);
                return;
            }

            if (cmd[0] == '!')
                cmd = cmd.Substring(1);

            lock (m_sync)
            {
                cmd = cmd.ToLower();
                if (m_commands.ContainsKey(cmd))
                {
                    m_commands.Remove(cmd);
                    m_remove.Add(cmd);
                    m_dirty = true;
                    sender.SendResponse(Importance.Low, string.Format("Removed command {0}.", cmd));
                }
                else
                {
                    sender.SendResponse(Importance.Med, string.Format("Command {0} not found.", cmd));
                }
            }
        }


        [BotCommand(AccessLevel.Mod, "addcom", "addcommand")]
        public void AddCommand(WinterBot sender, TwitchUser user, string c, string v)
        {
            Args args = v.ParseArguments(m_bot);

            AccessLevel level = args.GetAccessFlag("ul", AccessLevel.Mod);
            string cmdName = args.GetOneWord();
            string cmdText = args.GetString();

            if (string.IsNullOrWhiteSpace(cmdName) || string.IsNullOrWhiteSpace(cmdText) || args.Error != null)
            {
                sender.SendResponse(Importance.Med, m_addCommandUsage);
                return;
            }

            if (cmdName[0] != '!')
            {
                sender.SendResponse(Importance.Med, string.Format("User commands must start with a '!'. {0}", m_addCommandUsage));
                return;
            }
            else
            {
                cmdName = cmdName.Substring(1);
            }

            if (cmdText[0] == '.' || cmdText[0] == '/')
            {
                sender.SendResponse(Importance.Med, string.Format("Cannot create a command which starts with a '{0}'.", cmdText[0]));
                return;
            }

            cmdName = cmdName.ToLower();
            Command userCommand = new Command(level, cmdText);

            bool exists;
            lock (m_sync)
            {
                exists = m_commands.ContainsKey(cmdName);
                m_commands[cmdName] = userCommand;
                m_dirty = true;
            }

            if (exists)
                sender.SendResponse(Importance.Med, string.Format("Updated command: !{0}.", cmdName));
            else
                sender.SendResponse(Importance.Med, string.Format("Successfully added command: !{0}.", cmdName));
        }

        void UnknownCommandReceived(WinterBot sender, TwitchUser user, string cmd, string value)
        {
            cmd = cmd.ToLower();
            Command command;

            lock (m_sync)
                if (!m_commands.TryGetValue(cmd, out command))
                    return;

            if (sender.CanUseCommand(user, command.AccessLevel) && CanSendCommand(cmd))
            {
                sender.SendResponse(Importance.Low, command.Text);
                m_sent.AddLast(new Tuple<string, DateTime>(cmd, DateTime.Now));
            }
        }

        private bool CanSendCommand(string cmd)
        {
            while (m_sent.Count > 0 && m_sent.First.Value.Item2.Elapsed().TotalSeconds >= TimeSpan)
                m_sent.RemoveFirst();

            foreach (var item in m_sent)
                if (item.Item1.Equals(cmd, StringComparison.CurrentCultureIgnoreCase) && item.Item2.Elapsed().TotalSeconds <= 5)
                    return false;

            return m_sent.Count < MaxMessages;
        }

        void Load(Stream stream)
        {
            if (stream == null)
            {
                Thread.Sleep(10000);
                HttpManager.Instance.GetAsync("api.php", "GETCMDS=1", Load);
                return;
            }

            lock (m_sync)
            {
                using (StreamReader reader = new StreamReader(stream))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        string[] values = line.Split(new char[] { ' ' }, 3);
                        if (values.Length != 3)
                            continue;

                        int access;
                        if (!int.TryParse(values[1], out access))
                            continue;

                        m_commands[values[0].ToLower()] = new Command((AccessLevel)access, values[2]);
                    }
                }
            }
        }


        public override void Save()
        {
            if (!m_dirty)
                return;

            StringBuilder sb = new StringBuilder();
            lock (m_sync)
            {
                foreach (var item in m_commands)
                    sb.AppendFormat("{0}\n{1}\n{2}\n", item.Key, (int)item.Value.AccessLevel, item.Value.Text);

                foreach (string cmd in m_remove)
                    sb.AppendFormat("{0}\nDELETE\n", cmd);

                m_remove.Clear();
                m_dirty = false;
            }

            HttpManager.Instance.PostAsync("api.php", "SETCMDS=1", sb).Wait();
        }

        class Command
        {
            public AccessLevel AccessLevel { get; private set; }
            public string Text { get; private set; }

            public Command(AccessLevel accessLevel, string value)
            {
                AccessLevel = accessLevel;
                Text = value;
            }

        }
    }
}
