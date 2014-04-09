using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter
{
    public abstract class CommandParam
    {
        public string Name
        {
            get;
            private set;
        }

        public CommandParam(string name)
        {
            Name = name;
        }
    }

    public class IntParam
    {
        public IntParam(string name)
        {

        }
    }

    public class ParamParser
    {
    }
}
