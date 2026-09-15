using System;
using System.Collections.Generic;
using WinUpdateManager.Core;

namespace WinUpdateManager.Output
{
    /// <summary>Console output helpers: colors, tables, prompts. Messages are mirrored to the log file.</summary>
    public static class ConsoleUi
    {
        private static readonly object Sync = new object();

        static ConsoleUi()
        {
            Log = Logger.Null;
        }

        /// <summary>Suppress informational output (used with --quiet and --json).</summary>
        public static bool Quiet { get; set; }

        public static bool NoColor { get; set; }

        public static Logger Log { get; set; }

        public static int Width
        {
            get
            {
                try
                {
                    int w = Console.WindowWidth;
                    return w >= 60 ? w : 120;
                }
                catch
                {
                    return 160;
                }
            }
        }

        public static void Info(string message)
        {
            Log.Info(message);
            if (!Quiet) WriteLine(message, null, false);
        }

        public static void Detail(string message)
        {
            Log.Info("  " + message);
            if (!Quiet) WriteLine("      " + message, ConsoleColor.Gray, false);
        }

        public static void Success(string message)
        {
            Log.Info(message);
            if (!Quiet) WriteLine(message, ConsoleColor.Green, false);
        }

        public static void Warn(string message)
        {
            Log.Warn(message);
            WriteLine("WARNING: " + message, ConsoleColor.Yellow, Quiet);
        }

        public static void Error(string message)
        {
            Log.Error(message);
            WriteLine("ERROR: " + message, ConsoleColor.Red, true);
        }

        /// <summary>Always written to standard output (used for JSON).</summary>
        public static void Raw(string text)
        {
            lock (Sync)
            {
                Console.Out.WriteLine(text);
            }
        }

        public static void Line(string text = "")
        {
            if (!Quiet) WriteLine(text, null, false);
        }

        public static void Colored(string text, ConsoleColor color)
        {
            if (!Quiet) WriteLine(text, color, false);
        }

        public static void Header(string title)
        {
            if (Quiet) return;
            WriteLine(string.Empty, null, false);
            WriteLine("== " + title + " ==", ConsoleColor.Cyan, false);
        }

        public static void KeyValue(string key, string value, ConsoleColor? color = null)
        {
            if (Quiet) return;
            lock (Sync)
            {
                Console.Out.Write("  " + key.PadRight(22) + ": ");
                WriteLineUnlocked(value ?? "-", color, Console.Out);
            }
        }

        /// <summary>Prints a table; the flex column is truncated so the table fits the console width.</summary>
        public static void Table(string[] headers, IList<string[]> rows, int flexColumn)
        {
            if (Quiet) return;
            int cols = headers.Length;
            var widths = new int[cols];
            for (int c = 0; c < cols; c++) widths[c] = headers[c].Length;
            foreach (var row in rows)
            {
                for (int c = 0; c < cols; c++)
                {
                    int len = c < row.Length && row[c] != null ? row[c].Length : 0;
                    if (len > widths[c]) widths[c] = len;
                }
            }

            int total = 2 + (cols - 1) * 2;
            for (int c = 0; c < cols; c++) total += widths[c];
            int available = Width - 1;
            if (flexColumn >= 0 && flexColumn < cols && total > available)
            {
                widths[flexColumn] = Math.Max(headers[flexColumn].Length, Math.Max(20, widths[flexColumn] - (total - available)));
            }

            lock (Sync)
            {
                WriteLineUnlocked(FormatRow(headers, widths), ConsoleColor.White, Console.Out);
                var sep = new string[cols];
                for (int c = 0; c < cols; c++) sep[c] = new string('-', widths[c]);
                WriteLineUnlocked(FormatRow(sep, widths), ConsoleColor.DarkGray, Console.Out);
                foreach (var row in rows) WriteLineUnlocked(FormatRow(row, widths), null, Console.Out);
            }
        }

        /// <summary>Asks a yes/no question. Returns null when no interactive input is available.</summary>
        public static bool? Confirm(string question, bool assumeYes)
        {
            if (assumeYes) return true;
            string answer = Prompt(question + " [y/N]");
            if (answer == null) return null;
            answer = answer.Trim().ToLowerInvariant();
            return answer == "y" || answer == "yes";
        }

        public static string Prompt(string question)
        {
            lock (Sync)
            {
                SetColor(ConsoleColor.Cyan);
                Console.Out.Write(question + " ");
                ResetColor();
            }
            try
            {
                return Console.ReadLine();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Waits for Enter. Returns false when input is closed.</summary>
        public static bool Pause()
        {
            return Prompt("\nPress Enter to continue...") != null;
        }

        private static string FormatRow(string[] cells, int[] widths)
        {
            var parts = new string[widths.Length];
            for (int c = 0; c < widths.Length; c++)
            {
                string cell = c < cells.Length && cells[c] != null ? cells[c] : string.Empty;
                cell = Text.Truncate(cell, widths[c]);
                parts[c] = c == widths.Length - 1 ? cell : cell.PadRight(widths[c]);
            }
            return "  " + string.Join("  ", parts);
        }

        private static void WriteLine(string text, ConsoleColor? color, bool toError)
        {
            lock (Sync)
            {
                WriteLineUnlocked(text, color, toError ? Console.Error : Console.Out);
            }
        }

        private static void WriteLineUnlocked(string text, ConsoleColor? color, System.IO.TextWriter writer)
        {
            if (color.HasValue) SetColor(color.Value);
            writer.WriteLine(text);
            if (color.HasValue) ResetColor();
        }

        private static void SetColor(ConsoleColor color)
        {
            if (NoColor) return;
            try
            {
                Console.ForegroundColor = color;
            }
            catch
            {
                // No console attached (scheduled task / redirected output).
            }
        }

        private static void ResetColor()
        {
            if (NoColor) return;
            try
            {
                Console.ResetColor();
            }
            catch
            {
                // ignore
            }
        }
    }
}
