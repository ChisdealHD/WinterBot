using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinterBotLogging
{
    [EventSource(Name = "WinterBot-Extensions")]
    public class WinterSource : EventSource
    {
        public static WinterSource Log = new WinterSource();

        [Event(1, Keywords = Keywords.Status, Task = Tasks.Http, Opcode = EventOpcode.Send)]
        public void PostHttp(string url, string result)
        {
            WriteEvent(1, url, result);
        }

        [Event(2, Keywords = Keywords.Status, Task = Tasks.Http, Opcode = EventOpcode.Receive)]
        public void GetHttp(string url, long bytes, string result)
        {
            WriteEvent(2, url, bytes, result);
        }


        public class Keywords
        {
            public const EventKeywords Status = (EventKeywords)0x0001;
        }
        public class Tasks
        {
            public const EventTask Http = (EventTask)1;
            public const EventTask Commands = (EventTask)2;
        }
    }
}
