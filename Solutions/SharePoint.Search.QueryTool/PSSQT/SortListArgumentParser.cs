using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PSSQT
{
    class SortListArgumentParser : StringListArgumentParser
    {
        private static readonly string sortDescending = ":descending";
        private static readonly string sortAscending = ":ascending";

        public SortListArgumentParser(List<string> list) :
            base(list)
        {
        }


        protected override List<string> NormalizeList(List<string> list, bool splitIndividualItems)
        {
            var sortListItems = base.NormalizeList(list, splitIndividualItems);

            List<string> results = new List<string>();

            foreach (var item in sortListItems)
            {
                if (item.EndsWith(sortDescending) || item.EndsWith(sortAscending))
                {
                    results.Add(item);
                }
                else
                {
                    var parts = item.Split(':');

                    if (parts.Length > 1)
                    {
                        if (sortAscending.StartsWith(String.Format(":{0}", parts[1])))
                        {
                            results.Add(String.Format("{0}{1}", parts[0], sortAscending));
                        }
                        else if (sortDescending.StartsWith(String.Format(":{0}", parts[1])))
                        {
                            results.Add(String.Format("{0}{1}", parts[0], sortDescending));
                        }
                        else
                        {
                            throw new Exception(String.Format("Unrecognized sort direction specifier: {0}, Use {1} or {2}", parts[1], sortAscending, sortDescending));
                        }
                    }
                    else
                    {
                        results.Add(String.Format("{0}{1}", item, sortAscending));
                    }
                }
            }

            return results;
        }

        protected override void AddItem(List<string> result, string stringitem)
        {
            // stringitem may contain ascending/descending qualifier
            var strparts = stringitem.Split(':');
            var property = strparts[0];

            if (property.Equals("Rank", StringComparison.InvariantCultureIgnoreCase))
            {
                if (strparts.Length > 1)
                {
                    result.Add($"Rank:{strparts[1]}");
                }
                else
                {
                    result.Add("Rank");   // todo: check this. For now we still do tolower()
                }
            }
            else if (property.Equals("DocId", StringComparison.InvariantCultureIgnoreCase))
            {
                if (strparts.Length > 1)
                {
                    result.Add($"[DocId]:{strparts[1]}");
                }
                else
                {
                    result.Add("[DocId]");   // todo: check this. For now we still do tolower()
                }
            }
            else
            {
                base.AddItem(result, stringitem);
            }
        }

        protected override void PostProcessList(List<string> result)
        {
            // do nothing
        }
    }
}
