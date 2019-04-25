using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace PSSQT.Helpers
{
    public static class Extensions
    {
        public static IDictionary<string, string[]> ToDictionary(this NameValueCollection source)
        {
            return source.AllKeys.ToDictionary(k => k, k => source.GetValues(k));
        }
    }
}
