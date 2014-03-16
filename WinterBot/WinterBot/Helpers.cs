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
    }
}
