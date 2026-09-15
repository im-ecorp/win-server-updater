using System;
using System.Collections.Generic;
using System.Globalization;

namespace WinUpdateManager.Cli
{
    /// <summary>Parses selections such as "1,3,5-7", "all" or "*" into 1-based indexes.</summary>
    public static class SelectionParser
    {
        public static List<int> Parse(string input, int max)
        {
            if (input == null) throw new FormatException("No selection entered.");
            string text = input.Trim();
            if (text.Length == 0) throw new FormatException("No selection entered.");

            var result = new List<int>();
            if (text == "*" || text.Equals("all", StringComparison.OrdinalIgnoreCase) || text.Equals("a", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 1; i <= max; i++) result.Add(i);
                return result;
            }

            var seen = new HashSet<int>();
            foreach (var rawPart in text.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string part = rawPart.Trim();
                int dash = part.IndexOf('-');
                int from, to;
                if (dash > 0)
                {
                    from = ParseNumber(part.Substring(0, dash), max);
                    to = ParseNumber(part.Substring(dash + 1), max);
                    if (from > to)
                    {
                        int tmp = from;
                        from = to;
                        to = tmp;
                    }
                }
                else
                {
                    from = to = ParseNumber(part, max);
                }

                for (int i = from; i <= to; i++)
                {
                    if (seen.Add(i)) result.Add(i);
                }
            }

            result.Sort();
            return result;
        }

        private static int ParseNumber(string value, int max)
        {
            int n;
            if (!int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                throw new FormatException("'" + value + "' is not a number.");
            if (n < 1 || n > max)
                throw new FormatException("Number " + n + " is out of range (1-" + max + ").");
            return n;
        }
    }
}
