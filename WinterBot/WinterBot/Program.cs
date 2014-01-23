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
            WinterBot bot = new WinterBot("", "", "");
            bot.Go();
        }
    }
}
