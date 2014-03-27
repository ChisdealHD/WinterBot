using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using Winter;
using System.Linq;

namespace WinterExtensions
{
    class Program
    {
        static TimeSpan m_lastHeartbeat = new TimeSpan();
        static volatile int s_messages = 0;
        static string[] m_autoban = new string[0];

        static void Main(string[] args)
        {
            string thisExe = Assembly.GetExecutingAssembly().Location;

            string iniFile = Path.Combine(Path.GetDirectoryName(thisExe), "options.ini");
            if (!File.Exists(iniFile))
            {
                MessageBox.Show(string.Format("Could not find options.ini.  This must be placed next to {0}.", Path.GetFileName(thisExe)), "No options.ini found!");
                Environment.Exit(1);
            }

            Options options = new Options(iniFile);
            if (string.IsNullOrEmpty(options.Channel) || string.IsNullOrEmpty(options.Username) || string.IsNullOrEmpty(options.Password))
            {
                MessageBox.Show("You must first fill in a stream, user, and oauth password to use WinterBot.\n\nPlease follow the instructions here:\nhttps://github.com/DarkAutumn/WinterBot/wiki/Getting-Started", "WinterBot needs configuration!");
                Environment.Exit(1);
            }

            string banlist = Path.Combine(options.DataDirectory, "ban.txt");
            if (File.Exists(banlist))
                m_autoban = File.ReadLines(banlist).ToArray();


            var verInfo = FileVersionInfo.GetVersionInfo(thisExe);
            string version = string.Format("{0}.{1}", verInfo.ProductMajorPart, verInfo.FileMinorPart);
            Console.WriteLine("Winterbot {0}", version);
            Console.WriteLine("Using data folder: {0}", GetDataFolder(options.DataDirectory));
            Console.WriteLine("Press Q to quit safely.");
            Console.WriteLine();

            WinterBot bot = new WinterBot(options, options.Channel, options.Username, options.Password);
            bot.AddCommands(new JukeBox(bot));
            bot.AddCommands(new Betting(bot));
            bot.AddCommands(new ViewerCountLogger(bot));
            bot.AddCommands(new Insult(bot));

            bot.ModeratorRemoved += delegate(WinterBot b, TwitchUser user) { WriteLine("Moderator removed: {0}", user.Name); };
            bot.ModeratorAdded += delegate(WinterBot b, TwitchUser user) { WriteLine("Moderator added: {0}", user.Name); };
            bot.Connected += delegate(WinterBot b) { WriteLine("Connected to channel: {0}", options.Channel); };
            bot.MessageReceived += bot_MessageReceived;
            bot.ChatClear += delegate(WinterBot b, TwitchUser user) { WriteLine("Chat Clear: {0}", user.Name); };
            bot.UserBanned += delegate(WinterBot b, TwitchUser user) { WriteLine("Banned: {0}", user.Name); };
            bot.UserTimedOut += delegate(WinterBot b, TwitchUser user, int duration) { WriteLine("Timeout: {0} for {1} seconds", user.Name, duration); };
            bot.DiagnosticMessage += delegate(WinterBot b, DiagnosticFacility f, string msg) { WriteLine("Diagnostics: {0}", msg); };
            bot.Tick += bot_Tick;

            Thread t = new Thread(bot.Go);
            t.Start();

            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e) { bot.Shutdown(); Environment.Exit(0); };

            while (true)
            {
                ConsoleKeyInfo key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Q)
                {
                    bot.Shutdown();
                    Environment.Exit(0);
                }
            }

        }

        static void bot_MessageReceived(WinterBot sender, TwitchUser user, string text)
        {
            s_messages++;

            if (m_autoban.Any(name => user.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase)))
                sender.Ban(user);
        }

        private static string GetDataFolder(string path)
        {
            string documentFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments).ToLower();
            if (path.ToLower().StartsWith(documentFolder))
                return "My Documents\\" + path.Substring(documentFolder.Length);

            return path;
        }

        static void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            m_lastHeartbeat += timeSinceLastUpdate;

            if (s_messages > 0 && m_lastHeartbeat.TotalMinutes >= 5)
            {
                WriteLine("Viewers: {0}, Messsages: {1}", sender.CurrentViewers, s_messages);
                s_messages = 0;
                m_lastHeartbeat = new TimeSpan();
            }
        }

        static void WriteLine(string fmt, params object[] objs)
        {
            Console.WriteLine("[{0}] {1}", DateTime.Now, string.Format(fmt, objs));
        }
    }
}
