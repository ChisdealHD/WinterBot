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
using WinterExtensions;

namespace Winter
{
    class Program
    {
        static TimeSpan m_lastHeartbeat = new TimeSpan();
        static volatile int s_messages = 0;

        static void Main(string[] args)
        {
            string iniFile = Path.Combine(Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), "options.ini");
            Options options = new Options(iniFile);

            WinterBot bot = new WinterBot(options, options.Channel, options.Username, options.Password);
            bot.AddCommands(new JukeBox(bot));
            bot.AddCommands(new Betting(bot));
            bot.AddCommands(new AnnoyFrosty(bot));

            bot.ModeratorRemoved += delegate(WinterBot b, TwitchUser user) { WriteLine("Moderator removed: {0}", user.Name); };
            bot.ModeratorAdded += delegate(WinterBot b, TwitchUser user) { WriteLine("Moderator added: {0}", user.Name); };
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
                    break;
                }
            }

        }

        static void bot_Tick(WinterBot sender, TimeSpan timeSinceLastUpdate)
        {
            m_lastHeartbeat += timeSinceLastUpdate;

            if (s_messages > 0 && m_lastHeartbeat.TotalMinutes >= 5)
            {
                WriteLine("Messsages: {0}", s_messages);
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
