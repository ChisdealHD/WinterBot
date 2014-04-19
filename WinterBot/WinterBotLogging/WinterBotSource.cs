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

        [Event(1, Keywords = Keywords.Events, Task = Tasks.Action, Opcode = EventOpcode.Start)]
        public void BeginAction(string name, string text)
        {
            WriteEvent(1, name, text);
        }

        [Event(2, Keywords = Keywords.Events, Task = Tasks.Action, Opcode = EventOpcode.Stop)]
        public void EndAction()
        {
            WriteEvent(2);
        }
        


        [Event(3, Keywords = Keywords.Events, Task = Tasks.Clear, Opcode = EventOpcode.Start)]
        public void BeginClear(string name)
        {
            WriteEvent(3, name);
        }

        [Event(4, Keywords = Keywords.Events, Task = Tasks.Clear, Opcode = EventOpcode.Stop)]
        public void EndClear()
        {
            WriteEvent(4);
        }

        [Event(5, Keywords = Keywords.Events, Task = Tasks.Message, Opcode = EventOpcode.Start)]
        public void BeginMessage(string name, string text)
        {
            WriteEvent(5, name, text);
        }

        [Event(6, Keywords = Keywords.Events, Task = Tasks.Message, Opcode = EventOpcode.Stop)]
        public void EndMessage()
        {
            WriteEvent(6);
        }



        [Event(7, Keywords = Keywords.Events, Task = Tasks.Mod, Opcode = EventOpcode.Start)]
        public void BeginMod(string mod, bool added)
        {
            WriteEvent(7, mod, added);
        } 

        [Event(8, Keywords = Keywords.Events, Task = Tasks.Mod, Opcode = EventOpcode.Stop)]
        public void EndMod()
        {
            WriteEvent(8);
        }

        
        [Event(9, Keywords = Keywords.Events, Task = Tasks.Sub, Opcode = EventOpcode.Start)]
        public void BeginSub(string name)
        {
            WriteEvent(9, name);
        }
        

        [Event(10, Keywords = Keywords.Events, Task = Tasks.Sub, Opcode = EventOpcode.Stop)]
        public void EndSub()
        {
            WriteEvent(10);
        }


        
        [Event(11, Keywords = Keywords.Events, Task = Tasks.Viewers, Opcode = EventOpcode.Start)]
        public void BeginViewers(int viewers)
        {
            WriteEvent(11, viewers);
        }


        [Event(12, Keywords = Keywords.Events, Task = Tasks.Viewers, Opcode = EventOpcode.Stop)]
        public void EndViewers()
        {
            WriteEvent(12);
        }

        
        [Event(13, Keywords = Keywords.Events, Task = Tasks.StreamStatus, Opcode = EventOpcode.Start)]
        public void BeginStreamStatus(bool online)
        {
            WriteEvent(13, online);
        }

        [Event(14, Keywords = Keywords.Events, Task = Tasks.StreamStatus, Opcode = EventOpcode.Stop)]
        public void EndStreamStatus()
        {
            WriteEvent(14);
        }


        
        [Event(15, Keywords = Keywords.Events, Task = Tasks.Follow, Opcode = EventOpcode.Start)]
        public void BeginFollow(string name)
        {
            WriteEvent(15, name);
        }

        [Event(16, Keywords = Keywords.Events, Task = Tasks.Follow, Opcode = EventOpcode.Stop)]
        public void EndFollow()
        {
            WriteEvent(16);
        }


        [Event(17, Keywords = Keywords.Events, Task = Tasks.Tick, Opcode = EventOpcode.Start)]
        public void BeginTick()
        {
            WriteEvent(17);
        }

        [Event(18, Keywords = Keywords.Events, Task = Tasks.Tick, Opcode = EventOpcode.Stop)]
        public void EndTick()
        {
            WriteEvent(18);
        }


        [Event(19, Keywords = Keywords.Status, Task = Tasks.Reconnect, Opcode = EventOpcode.Start)]
        public void BeginReconnect()
        {
            WriteEvent(19);
        }

        [Event(20, Keywords = Keywords.Status, Task = Tasks.Reconnect, Opcode = EventOpcode.Stop)]
        public void EndReconnect()
        {
            WriteEvent(20);
        }




        [Event(21, Keywords = Keywords.Events, Task = Tasks.Command, Opcode = EventOpcode.Start)]
        public void BeginCommand(string user, string command, string args)
        {
            WriteEvent(21, user, command, args);
        }

        [Event(22, Keywords = Keywords.Events, Task = Tasks.Command, Opcode = EventOpcode.Stop)]
        public void EndCommand()
        {
            WriteEvent(22);
        }



        [Event(23, Keywords = Keywords.Events, Task = Tasks.UnknownCommand, Opcode = EventOpcode.Start)]
        public void BeginUnknownCommand(string user, string command, string args)
        {
            WriteEvent(23, user, command, args);
        }

        [Event(24, Keywords = Keywords.Events, Task = Tasks.UnknownCommand, Opcode = EventOpcode.Stop)]
        public void EndUnknownCommand()
        {
            WriteEvent(24);
        }
        
        [Event(25, Keywords = Keywords.Status)]
        public void Connected(string channel)
        {
            WriteEvent(25, channel);
        }

        [Event(26, Keywords = Keywords.Events, Task = Tasks.StreamStatus, Opcode = EventOpcode.Start)]
        public void CheckStreamStatus(bool result)
        {
            WriteEvent(26, result);
        }
        
        [Event(27, Keywords = Keywords.Status, Task = Tasks.Command)]
        public void DenyCommand(string name, string cmd)
        {
            WriteEvent(27, name, cmd);
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
            public const EventTask Action = (EventTask)1;
            public const EventTask Clear = (EventTask)2;
            public const EventTask Message = (EventTask)3;
            public const EventTask Mod = (EventTask)4;
            public const EventTask Sub = (EventTask)5;
            public const EventTask Viewers = (EventTask)6;
            public const EventTask StreamStatus = (EventTask)7;
            public const EventTask Follow = (EventTask)8;
            public const EventTask Tick = (EventTask)9;
            public const EventTask Command = (EventTask)10;
            public const EventTask UnknownCommand = (EventTask)11;
            public const EventTask Reconnect = (EventTask)12;
        }
    }
}
