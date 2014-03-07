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
    class Program
    {
        static void Main(string[] args)
        {
            string iniFile = Path.Combine(Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName), "options.ini");
            Options options = new Options(iniFile);

            WinterBot bot = new WinterBot(options, options.Channel, options.Username, options.Password);
            bot.Go();
        }
    }
}
