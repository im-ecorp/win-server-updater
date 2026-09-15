using System;
using System.Globalization;
using System.IO;
using WinUpdateManager.Core;

namespace WinUpdateManager.Output
{
    /// <summary>Simple append-only log file. Logging failures never stop the program.</summary>
    public sealed class Logger
    {
        public static readonly Logger Null = new Logger(null);

        private readonly object sync = new object();
        private bool failed;

        public Logger(string filePath)
        {
            FilePath = filePath;
        }

        public string FilePath { get; private set; }

        public static string DefaultFilePath()
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            if (Text.IsBlank(root)) root = Path.GetTempPath();
            string dir = Path.Combine(Path.Combine(root, "WinUpdateManager"), "logs");
            return Path.Combine(dir, "WinUpdateManager-" + DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture) + ".log");
        }

        public void Info(string message)
        {
            Write("INFO ", message);
        }

        public void Warn(string message)
        {
            Write("WARN ", message);
        }

        public void Error(string message)
        {
            Write("ERROR", message);
        }

        private void Write(string level, string message)
        {
            if (FilePath == null || failed) return;
            lock (sync)
            {
                try
                {
                    string dir = Path.GetDirectoryName(FilePath);
                    if (!Text.IsBlank(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                    File.AppendAllText(FilePath,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) +
                        " [" + level + "] " + message + Environment.NewLine);
                }
                catch (Exception)
                {
                    failed = true;
                }
            }
        }
    }
}
