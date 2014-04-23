using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using System.Linq;
using System.Collections.Generic;

namespace Winter
{

    class Program
    {
        static DateTime s_lastHeartbeat = DateTime.Now;
        static volatile int s_messages = 0;

        static void Main(string[] args)
        {
            if (!Debugger.IsAttached)
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            string botDll = typeof(WinterBot).Assembly.Location;
            var verInfo = FileVersionInfo.GetVersionInfo(botDll);
            string version = string.Format("{0}.{1}", verInfo.ProductMajorPart, verInfo.FileMinorPart);

            Console.WriteLine("Winterbot {0}", version);
            Console.WriteLine("Press Q to quit safely.");
            Console.WriteLine();

            List<WinterBotInstance> bots = ParseArgs(args);

            Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e) { ShutdownBots(bots); Environment.Exit(0); };

            foreach (var bot in bots)
                bot.Start();

            Thread.Sleep(1000);
            while (true)
            {
                // We have to check login failures in the main loop because a bot can disconnect/reconnect due to network issues
                // and the subsequent connections could fail with login issues.

                for (int i = 0; i < bots.Count; i++)
                {
                    if (bots[i].FailedLogin)
                    {
                        var options = bots[i].Options;
                        Console.WriteLine("Failed login for account {0} (channel {1}).", options.Username, options.Channel);
                        bots.RemoveAt(i);
                        i--;
                    }
                    else if (!bots[i].Running)
                    {
                        // This shouldn't happen...
                        var options = bots[i].Options;
                        Console.WriteLine("Bot for channel {0} not running!", options.Channel);
                        bots.RemoveAt(i);
                        i--;
                    }
                }

                if (TryReadKey() == ConsoleKey.Q)
                {
                    ShutdownBots(bots);
                    Environment.Exit(0);
                }
            }
        }


        static ConsoleKey TryReadKey()
        {
            int max = 5000;
            const int sleep = 250;

            while (max > 0)
            {
                if (Console.KeyAvailable)
                    return Console.ReadKey(true).Key;

                Thread.Sleep(sleep);
                max -= sleep;
            }

            return default(ConsoleKey);
        }

        private static void ShutdownBots(List<WinterBotInstance> bots)
        {
            foreach (var bot in bots)
                bot.Stop();

            foreach (var bot in bots)
                bot.Join();
        }

        private static List<WinterBotInstance> ParseArgs(string[] args)
        {
            List<WinterBotInstance> result = new List<WinterBotInstance>();
            if (args.Length == 0)
            {
                string iniFile = GetIniFile("options.ini");
                if (iniFile == null)
                {
                    Usage();
                    Environment.Exit(1);
                }

                Options options = new Options(iniFile);
                CheckOptions(options);

                result.Add(new WinterBotInstance(options));
            }
            else
            {
                foreach (var arg in args)
                {
                    var iniFile = GetIniFile(arg);
                    if (iniFile == null)
                        Environment.Exit(1);

                    Options options = new Options(iniFile);
                    CheckOptions(options);

                    result.Add(new WinterBotInstance(options));
                }

                if (result.Count == 0)
                    Environment.Exit(1);
            }

            return result;
        }

        private static void CheckOptions(Options options)
        {
            if (string.IsNullOrEmpty(options.Channel) || string.IsNullOrEmpty(options.Username) || string.IsNullOrEmpty(options.Password))
            {
                MessageBox.Show("You must first fill in a stream, user, and oauth password to use WinterBot.\n\nPlease follow the instructions here:\nhttps://github.com/DarkAutumn/WinterBot/wiki/Getting-Started", "WinterBot needs configuration!");
                Environment.Exit(1);
            }
        }

        private static void LoadPlugins(Options options, WinterBot bot)
        {
            foreach (string fn in options.Plugins)
            {
                string filename = fn;
                if (!File.Exists(filename))
                {
                    Console.WriteLine("Could not find plugin file: {0}", filename);
                    continue;
                }

                filename = Path.GetFullPath(filename);
                try
                {
                    var assembly = Assembly.LoadFile(filename);

                    var types = from type in assembly.GetTypes()
                                where type.IsPublic

                                let attr = type.GetCustomAttribute(typeof(WinterBotPluginAttribute), false)
                                where attr != null
                                select type;

                    foreach (var type in types)
                    {
                        var init = type.GetMethod("Init", new Type[] { typeof(WinterBot) });
                        init.Invoke(null, new object[] { bot });
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error loading assembly {0}:", filename);
                    Console.WriteLine(e);
                }
            }
        }

        private static string GetIniFile(string arg)
        {
            string thisExe = Assembly.GetExecutingAssembly().Location;
            string thisFolder = Path.GetDirectoryName(thisExe);

            if (!arg.EndsWith(".ini"))
                arg += ".ini";

            if (File.Exists(arg))
                return arg;

            arg = Path.Combine(thisFolder, arg);
            if (File.Exists(arg))
                return arg;

            string longname = Path.Combine(thisFolder, Path.GetFileNameWithoutExtension(arg) + "_options.ini");
            if (!File.Exists(longname))
            {
                MessageBox.Show(string.Format("Cannot find file {0}", longname));
                Usage();
            }
                    
            return longname;
        }

        private static void Usage()
        {
            MessageBox.Show(string.Format("Usage: {0} [options.ini].", Path.GetFileName(Assembly.GetExecutingAssembly().Location)), "Invalid Parameters");
            Environment.Exit(1);
        }

        static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                // This will drop an "error.txt" file if we encounter an unhandled exception.
                string myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

                Exception obj = (Exception)e.ExceptionObject;
                string text = string.Format("{0}: {1}\n{2}", obj.GetType().ToString(), obj.Message, obj.StackTrace.ToString());

                using (var file = File.CreateText(Path.Combine(myDocuments, "winterbot_error.txt")))
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
                return "My Documents" + path.Substring(documentFolder.Length);

            return path;
        }

        static void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            if (s_messages > 0 && s_lastHeartbeat.Elapsed().TotalMinutes >= 5)
            {
                if (sender.IsStreamLive)
                    WriteLine("Messsages: {0,3} Viewers: {1,3}", s_messages, sender.CurrentViewers);
                else
                    WriteLine("Messsages: {0}", s_messages);

                s_messages = 0;
                s_lastHeartbeat = DateTime.Now;
            }
        }

        static void WriteLine(string fmt, params object[] objs)
        {
            Console.WriteLine("[{0}] {1}", DateTime.Now, string.Format(fmt, objs));
        }
    }


    class WinterBotInstance
    {
        static int s_count = 0;
        WinterBot m_bot;
        int m_messages;
        DateTime m_lastHeartbeat = DateTime.Now;
        Thread m_thread;

        public bool FailedLogin { get; private set; }
        public bool Running { get; private set; }
        public Options Options { get; private set; }

        public WinterBotInstance(Options options)
        {
            Interlocked.Increment(ref s_count);

            Options = options;

            m_bot = new WinterBot(options, options.Channel, options.Username, options.Password);
            LoadPlugins(options, m_bot);

            m_bot.Connected += delegate(WinterBot b) { WriteLine("Connected to channel: {0}", options.Channel); };
            m_bot.Disconnected += delegate(WinterBot b) { WriteLine("Disconnected."); };
            m_bot.MessageReceived += delegate(WinterBot b, TwitchUser user, string text) { m_messages++; };
            m_bot.ChatClear += delegate(WinterBot b, TwitchUser user) { WriteLine("Chat Clear: {0}", user.Name); };
            m_bot.UserBanned += delegate(WinterBot b, TwitchUser user) { WriteLine("Banned: {0}", user.Name); };
            m_bot.UserTimedOut += delegate(WinterBot b, TwitchUser user, int duration) { WriteLine("Timeout: {0} for {1} seconds", user.Name, duration); };
            m_bot.DiagnosticMessage += delegate(WinterBot b, DiagnosticFacility f, string msg) { if (f != DiagnosticFacility.IO) WriteLine("Diagnostics: {0}", msg); };
            m_bot.StreamOnline += delegate(WinterBot b) { WriteLine("Stream online."); };
            m_bot.StreamOffline += delegate(WinterBot b) { WriteLine("Stream offline."); };
            m_bot.Tick += bot_Tick;
        }

        public void Start()
        {
            if (m_thread != null)
                throw new InvalidOperationException("Bot already started.");

            Running = true;
            m_thread = new Thread(ThreadProc);
            m_thread.Start();
        }

        public void Stop()
        {
            m_bot.Shutdown();
        }

        public void Join()
        {
            var thread = m_thread;
            if (thread != null)
                m_thread.Join();
        }

        void ThreadProc()
        {
            try
            {
                m_bot.Go();
            }
            catch (TwitchLoginException)
            {
                FailedLogin = true;
            }

            m_thread = null;
            Running = false;
        }

        private static void LoadPlugins(Options options, WinterBot bot)
        {
            foreach (string fn in options.Plugins)
            {
                string filename = fn;
                if (!File.Exists(filename))
                {
                    Console.WriteLine("Could not find plugin file: {0}", filename);
                    continue;
                }

                filename = Path.GetFullPath(filename);
                try
                {
                    var assembly = Assembly.LoadFile(filename);

                    var types = from type in assembly.GetTypes()
                                where type.IsPublic

                                let attr = type.GetCustomAttribute(typeof(WinterBotPluginAttribute), false)
                                where attr != null
                                select type;

                    foreach (var type in types)
                    {
                        var init = type.GetMethod("Init", new Type[] { typeof(WinterBot) });
                        init.Invoke(null, new object[] { bot });
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("Error loading assembly {0}:", filename);
                    Console.WriteLine(e);
                }
            }
        }

        void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            if (m_messages > 0 && m_lastHeartbeat.Elapsed().TotalMinutes >= 5)
            {
                if (sender.IsStreamLive)
                    WriteLine("Messsages: {0,3} Viewers: {1,3}", m_messages, sender.CurrentViewers);
                else
                    WriteLine("Messsages: {0}", m_messages);

                m_messages = 0;
                m_lastHeartbeat = DateTime.Now;
            }
        }

        void WriteLine(string fmt, params object[] objs)
        {
            string channel = "";
            if (s_count > 1)
                channel = m_bot.Channel + ": ";

            Console.WriteLine("[{0}] {1}{2}", DateTime.Now, channel, string.Format(fmt, objs));
        }
    }
}
