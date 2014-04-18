using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WinterBotLogging
{
    [EventSource(Name = "WinterBot-WinterBot")]
    public class WinterBotSource : EventSource
    {
        public static WinterBotSource Log = new WinterBotSource();

        [Event(1, Keywords = Keywords.Status)]
        public void Connected(string channel)
        {
            WriteEvent(1, channel);
        }

        [Event(2, Keywords = Keywords.Events, Task = Tasks.Event, Opcode = EventOpcode.Stop)]
        public void EndEvent(int id)
        {
            WriteEvent(2, id);
        }

        [Event(3, Keywords = Keywords.Events, Task = Tasks.Event, Opcode = EventOpcode.Start)]
        public void BeginAction(string name, string text)
        {
            WriteEvent(3, name, text);
        }
        
        [Event(4, Keywords = Keywords.Events, Task = Tasks.Event, Opcode = EventOpcode.Start)]
        public void BeginClear(string name)
        {
            WriteEvent(4, name);
        } 

        [Event(5, Keywords = Keywords.Events, Task = Tasks.Event, Opcode = EventOpcode.Start)]
        public void BeginMessage(string name, string text)
        {
            WriteEvent(5, name, text);
        } 

        [Event(6, Keywords = Keywords.Events, Task = Tasks.Event, Opcode = EventOpcode.Start)]
        public void BeginMod(string mod, bool added)
        {
            WriteEvent(6, mod, added);
        } 
        
        [Event(7, Keywords = Keywords.Events, Task = Tasks.Event, Opcode = EventOpcode.Start)]
        public void BeginSub(string name)
        {
            WriteEvent(7, name);
        } 
        
        [Event(8, Keywords = Keywords.Events, Task = Tasks.Event, Opcode = EventOpcode.Start)]
        public void BeginViewers(int viewers)
        {
            WriteEvent(8, viewers);
        }
        
        [Event(9, Keywords = Keywords.Events, Task = Tasks.Event, Opcode = EventOpcode.Start)]
        public void BeginStreamStatus(bool online)
        {
            WriteEvent(9, online);
        } 
        
        [Event(10, Keywords = Keywords.Events, Task = Tasks.Event, Opcode = EventOpcode.Start)]
        public void BeginFollow(string name)
        {
            WriteEvent(10, name);
        }

        [Event(11, Keywords = Keywords.Events, Task = Tasks.Event, Opcode = EventOpcode.Start)]
        public void BeginTick()
        {
            WriteEvent(11);
        }


        [Event(12, Keywords = Keywords.Status, Task = Tasks.Event, Opcode = EventOpcode.Start)]
        public void BeginReconnect()
        {
            WriteEvent(12);
        }

        [Event(13, Keywords = Keywords.Status, Task = Tasks.Event, Opcode = EventOpcode.Stop)]
        public void EndReconnect()
        {
            WriteEvent(13);
        }

        [Event(14, Keywords = Keywords.Status, Task = Tasks.Event)]
        public void DenyCommand(string name, string cmd)
        {
            WriteEvent(14, name, cmd);
        }


        [Event(15, Keywords = Keywords.Events, Task = Tasks.Event, Opcode = EventOpcode.Start)]
        public void BeginCommand()
        {
            WriteEvent(15);
        }


        [Event(16, Keywords = Keywords.Events, Task = Tasks.Event, Opcode = EventOpcode.Start)]
        public void BeginUnknownCommand()
        {
            WriteEvent(16);
        }



        [Event(17, Keywords = Keywords.Events, Task = Tasks.Event, Opcode = EventOpcode.Start)]
        public void CheckStreamStatus(bool result)
        {
            WriteEvent(17, result);
        }


        public class EventId
        {
            public const int Action = 3;
            public const int Clear = 4;
            public const int Message = 5;
            public const int Mod = 6;
            public const int Sub = 7;
            public const int Viewers = 8;
            public const int StreamStatus = 9;
            public const int Follow = 10;
            public const int Tick = 11;
            public const int Command = 15;
            public const int UnknownCommand = 16;
        }


        public class Keywords
        {
            public const EventKeywords Events = (EventKeywords)0x0001;
            public const EventKeywords Status = (EventKeywords)0x0002;
        }
        public class Tasks
        {
            public const EventTask Event = (EventTask)0x1;
        }
    }
}
