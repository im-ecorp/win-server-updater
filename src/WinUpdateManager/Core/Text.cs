using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WinUpdateManager.Core
{
    /// <summary>
    /// String helpers that only use APIs available in .NET Framework 3.5.
    /// </summary>
    public static class Text
    {
        private static readonly Regex KbRegex = new Regex(@"KB\s?(\d{5,8})", RegexOptions.IgnoreCase);

        public static bool IsBlank(string value)
        {
            if (value == null) return true;
            foreach (char c in value)
            {
                if (!char.IsWhiteSpace(c)) return false;
            }
            return true;
        }

        public static string Join<T>(string separator, IEnumerable<T> items)
        {
            var sb = new StringBuilder();
            bool first = true;
            foreach (var item in items)
            {
                if (!first) sb.Append(separator);
                sb.Append(item);
                first = false;
            }
            return sb.ToString();
        }

        public static List<string> SplitList(string value)
        {
            var result = new List<string>();
            if (IsBlank(value)) return result;
            foreach (var part in value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) result.Add(trimmed);
            }
            return result;
        }

        /// <summary>"KB5030216", "kb 5030216" and "5030216" all become "5030216".</summary>
        public static string NormalizeKb(string kb)
        {
            if (kb == null) return string.Empty;
            var t = kb.Trim();
            if (t.StartsWith("KB", StringComparison.OrdinalIgnoreCase)) t = t.Substring(2);
            return t.Trim();
        }

        public static string ExtractKb(string title)
        {
            if (title == null) return null;
            var m = KbRegex.Match(title);
            return m.Success ? m.Groups[1].Value : null;
        }

        public static bool ContainsIgnoreCase(string haystack, string needle)
        {
            return haystack != null && needle != null &&
                   haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool EqualsIgnoreCase(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        public static string Truncate(string value, int max)
        {
            if (value == null || max <= 0) return string.Empty;
            if (value.Length <= max) return value;
            if (max <= 3) return value.Substring(0, max);
            return value.Substring(0, max - 3) + "...";
        }

        public static string FormatSize(decimal bytes)
        {
            if (bytes <= 0) return "-";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            decimal v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1)
            {
                v /= 1024;
                u++;
            }
            return v.ToString(u == 0 ? "0" : "0.0", CultureInfo.InvariantCulture) + " " + units[u];
        }

        public static string FormatDate(DateTime? value)
        {
            return value.HasValue
                ? value.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : "-";
        }

        public static string FormatDuration(TimeSpan span)
        {
            if (span.TotalDays >= 1) return string.Format(CultureInfo.InvariantCulture, "{0}d {1}h", (int)span.TotalDays, span.Hours);
            if (span.TotalHours >= 1) return string.Format(CultureInfo.InvariantCulture, "{0}h {1}m", (int)span.TotalHours, span.Minutes);
            if (span.TotalMinutes >= 1) return string.Format(CultureInfo.InvariantCulture, "{0}m {1}s", (int)span.TotalMinutes, span.Seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}s", (int)Math.Max(0, span.TotalSeconds));
        }

        public static string Hex(int value)
        {
            return "0x" + value.ToString("X8", CultureInfo.InvariantCulture);
        }

        public static string YesNo(bool value)
        {
            return value ? "Yes" : "No";
        }

        /// <summary>Quotes a command-line argument using the rules of CommandLineToArgvW.</summary>
        public static string QuoteArgument(string arg)
        {
            if (arg == null) return "\"\"";
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '"' }) < 0) return arg;

            var sb = new StringBuilder("\"");
            int backslashes = 0;
            foreach (char c in arg)
            {
                if (c == '\\')
                {
                    backslashes++;
                    continue;
                }
                if (c == '"')
                {
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(c);
                }
                backslashes = 0;
            }
            sb.Append('\\', backslashes * 2);
            sb.Append('"');
            return sb.ToString();
        }

        public static int Levenshtein(string a, string b)
        {
            a = a ?? string.Empty;
            b = b ?? string.Empty;
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;
            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                }
            }
            return d[a.Length, b.Length];
        }
    }
}
