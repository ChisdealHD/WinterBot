using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace Winter
{
    class Program
    {
        static TimeSpan s_lastHeartbeat = new TimeSpan();
        static volatile int s_messages = 0;
        static string s_errorFile;

        static void Main(string[] args)
        {
            if (!Debugger.IsAttached)
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

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

            s_errorFile = options.DataDirectory;

            var verInfo = FileVersionInfo.GetVersionInfo(thisExe);
            string version = string.Format("{0}.{1}", verInfo.ProductMajorPart, verInfo.FileMinorPart);

            Console.WriteLine("Winterbot {0}", version);
            Console.WriteLine("Using data folder: {0}", GetDataFolder(options.DataDirectory));
            Console.WriteLine("Press Q to quit safely.");
            Console.WriteLine();

            WinterBot bot = new WinterBot(options, options.Channel, options.Username, options.Password);

            bot.Connected += delegate(WinterBot b) { WriteLine("Connected to channel: {0}", options.Channel); };
            bot.MessageReceived += delegate(WinterBot b, TwitchUser user, string text) { s_messages++; };
            bot.ChatClear += delegate(WinterBot b, TwitchUser user) { WriteLine("Chat Clear: {0}", user.Name); };
            bot.UserBanned += delegate(WinterBot b, TwitchUser user) { WriteLine("Banned: {0}", user.Name); };
            bot.UserTimedOut += delegate(WinterBot b, TwitchUser user, int duration) { WriteLine("Timeout: {0} for {1} seconds", user.Name, duration); };
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

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (string.IsNullOrEmpty(s_errorFile))
                s_errorFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "WinterBot");

            try
            {
                // This will drop an "error.txt" file if we encounter an unhandled exception.
                Exception obj = (Exception)e.ExceptionObject;
                string text = string.Format("{0}: {1}\n{2}", obj.GetType().ToString(), obj.Message, obj.StackTrace.ToString());

                using (var file = File.CreateText(Path.Combine(s_errorFile, "winterbot_error.txt")))
                    file.WriteLine(text);

                MessageBox.Show(text, "Unhandled Exception");
            }
            catch
            {
                // Ignore errors here.
            }
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
            s_lastHeartbeat += timeSinceLastUpdate;

            if (s_messages > 0 && s_lastHeartbeat.TotalMinutes >= 5)
            {
                WriteLine("Messsages: {0}", s_messages);
                s_messages = 0;
                s_lastHeartbeat = new TimeSpan();
            }
        }

        static void WriteLine(string fmt, params object[] objs)
        {
            Console.WriteLine("[{0}] {1}", DateTime.Now, string.Format(fmt, objs));
        }
    }
}
