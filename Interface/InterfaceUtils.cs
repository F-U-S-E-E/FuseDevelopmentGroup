using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FUSE.Interface
{
    internal static class InterfaceUtils
    {
        public static int SafeCount(Func<int> count)
        {
            try
            {
                return Math.Max(0, count());
            }
            catch
            {
                return 0;
            }
        }
    }
}
