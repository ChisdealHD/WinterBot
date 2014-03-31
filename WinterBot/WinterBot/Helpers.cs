using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Winter
{
    public static class Extensions
    {
        public static TimeSpan Elapsed(this DateTime self)
        {
            return DateTime.Now - self;
        }

        public static IEnumerable<T> Enumerate<T>(this ConcurrentQueue<T> self)
        {
            T value;
            while (self.TryDequeue(out value))
                yield return value;
        }


        public static bool ParseBool(this string self, ref bool result)
        {
            if (bool.TryParse(self, out result))
                return true;

            result = true;
            if (self.Equals("true", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("t", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("yes", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("y", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("1", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("enable", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("enabled", StringComparison.CurrentCultureIgnoreCase))
                return true;

            result = false;
            if (self.Equals("false", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("f", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("no", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("n", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("0", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("disable", StringComparison.CurrentCultureIgnoreCase))
                return true;
            else if (self.Equals("disabled", StringComparison.CurrentCultureIgnoreCase))
                return true;

            return false;
        }
    }
}
