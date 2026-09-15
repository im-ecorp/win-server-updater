using System;
using System.Globalization;

namespace WinUpdateManager.Core
{
    /// <summary>
    /// Windows Update policy values stored under HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate[\AU].
    /// Null means "not configured".
    /// </summary>
    public sealed class AutoUpdatePolicy
    {
        // AU key
        public int? NoAutoUpdate { get; set; }
        public int? AUOptions { get; set; }
        public int? ScheduledInstallDay { get; set; }
        public int? ScheduledInstallTime { get; set; }
        public int? UseWUServer { get; set; }

        // WindowsUpdate key
        public string WUServer { get; set; }
        public string WUStatusServer { get; set; }
        public string TargetGroup { get; set; }
        public int? TargetGroupEnabled { get; set; }
        public int? DoNotConnectToWindowsUpdateInternetLocations { get; set; }

        public bool UsesWsus
        {
            get { return UseWUServer == 1 && !Text.IsBlank(WUServer); }
        }

        public bool IsConfigured
        {
            get
            {
                return NoAutoUpdate.HasValue || AUOptions.HasValue || ScheduledInstallDay.HasValue ||
                       ScheduledInstallTime.HasValue || UseWUServer.HasValue || WUServer != null ||
                       WUStatusServer != null || TargetGroup != null || TargetGroupEnabled.HasValue ||
                       DoNotConnectToWindowsUpdateInternetLocations.HasValue;
            }
        }

        public AutoUpdatePolicy Clone()
        {
            return (AutoUpdatePolicy)MemberwiseClone();
        }

        public string DescribeMode()
        {
            if (NoAutoUpdate == 1) return "Disabled (automatic updates turned off by policy)";
            if (!AUOptions.HasValue) return "Not configured (Windows default behaviour)";
            switch (AUOptions.Value)
            {
                case 2: return "Notify before download and install";
                case 3: return "Download automatically, notify before install";
                case 4: return "Download automatically and install on schedule (" + DescribeSchedule() + ")";
                case 5: return "Local administrator chooses the setting";
                case 7: return "Download automatically, notify to install, notify to restart";
                default: return "Unknown AUOptions value " + AUOptions.Value;
            }
        }

        public string DescribeSchedule()
        {
            string day = PolicyParsing.DayName(ScheduledInstallDay ?? 0);
            string time = (ScheduledInstallTime ?? 3).ToString("00", CultureInfo.InvariantCulture) + ":00";
            return day + " at " + time;
        }

        public string DescribeSource()
        {
            if (UsesWsus) return "WSUS (" + WUServer + ")";
            if (UseWUServer == 1) return "WSUS enabled but WUServer is empty (misconfigured)";
            return "Windows Update / Microsoft Update (internet)";
        }
    }

    public sealed class PolicyChange
    {
        public int? Mode { get; set; }              // 1 = disabled, 2..5 = AUOptions
        public int? Day { get; set; }               // 0 = every day, 1..7 = Sunday..Saturday
        public int? Hour { get; set; }              // 0..23
        public string WsusServer { get; set; }
        public string WsusStatusServer { get; set; }
        public string TargetGroup { get; set; }
        public bool ClearWsus { get; set; }
        public bool ResetAll { get; set; }

        public bool IsEmpty
        {
            get
            {
                return !Mode.HasValue && !Day.HasValue && !Hour.HasValue && WsusServer == null &&
                       WsusStatusServer == null && TargetGroup == null && !ClearWsus && !ResetAll;
            }
        }

        /// <summary>Returns a new policy with this change applied.</summary>
        public AutoUpdatePolicy ApplyTo(AutoUpdatePolicy current)
        {
            var p = ResetAll ? new AutoUpdatePolicy() : current.Clone();
            if (ResetAll) return p;

            if (Mode.HasValue)
            {
                if (Mode.Value == 1)
                {
                    p.NoAutoUpdate = 1;
                }
                else
                {
                    p.NoAutoUpdate = 0;
                    p.AUOptions = Mode.Value;
                }
            }

            if (Day.HasValue) p.ScheduledInstallDay = Day.Value;
            if (Hour.HasValue) p.ScheduledInstallTime = Hour.Value;

            if ((Day.HasValue || Hour.HasValue) && !Mode.HasValue && p.AUOptions != 4)
            {
                // A schedule only makes sense with "auto download and schedule install".
                p.NoAutoUpdate = 0;
                p.AUOptions = 4;
            }
            if (p.AUOptions == 4)
            {
                if (!p.ScheduledInstallDay.HasValue) p.ScheduledInstallDay = 0;
                if (!p.ScheduledInstallTime.HasValue) p.ScheduledInstallTime = 3;
            }

            if (ClearWsus)
            {
                p.WUServer = null;
                p.WUStatusServer = null;
                p.UseWUServer = null;
                p.TargetGroup = null;
                p.TargetGroupEnabled = null;
            }

            if (WsusServer != null)
            {
                p.WUServer = WsusServer;
                p.WUStatusServer = WsusStatusServer ?? WsusServer;
                p.UseWUServer = 1;
            }
            else if (WsusStatusServer != null)
            {
                p.WUStatusServer = WsusStatusServer;
            }

            if (TargetGroup != null)
            {
                if (TargetGroup.Length == 0)
                {
                    p.TargetGroup = null;
                    p.TargetGroupEnabled = null;
                }
                else
                {
                    p.TargetGroup = TargetGroup;
                    p.TargetGroupEnabled = 1;
                }
            }
            return p;
        }
    }

    public static class PolicyParsing
    {
        private static readonly string[] Days = { "Every day", "Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday" };

        public static string DayName(int day)
        {
            return day >= 0 && day < Days.Length ? Days[day] : "Day " + day;
        }

        /// <summary>Returns 1 for disabled or the AUOptions value (2-5).</summary>
        public static int ParseMode(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "1":
                case "off":
                case "disable":
                case "disabled":
                    return 1;
                case "2":
                case "notify":
                    return 2;
                case "3":
                case "download":
                case "auto-download":
                    return 3;
                case "4":
                case "auto":
                case "install":
                case "scheduled":
                    return 4;
                case "5":
                case "local":
                case "local-admin":
                    return 5;
                default:
                    throw new UsageException("Invalid --mode '" + value + "'. Use: disabled, notify, download, scheduled, local-admin.");
            }
        }

        /// <summary>Returns 0 for every day, 1..7 for Sunday..Saturday.</summary>
        public static int ParseDay(string value)
        {
            var v = (value ?? string.Empty).Trim().ToLowerInvariant();
            int n;
            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= 0 && n <= 7) return n;
            switch (v)
            {
                case "every":
                case "everyday":
                case "daily":
                case "all":
                    return 0;
            }
            for (int i = 1; i < Days.Length; i++)
            {
                if (v.Length >= 3 && Days[i].StartsWith(v, StringComparison.OrdinalIgnoreCase)) return i;
            }
            throw new UsageException("Invalid day '" + value + "'. Use: every, sunday..saturday (or sun..sat), or 0-7.");
        }

        public static DayOfWeek ToDayOfWeek(int policyDay)
        {
            if (policyDay < 1 || policyDay > 7) throw new UsageException("A weekly schedule needs a specific day (sunday..saturday).");
            return (DayOfWeek)(policyDay - 1);
        }

        /// <summary>Parses "3", "03", "3:30", "03:30" into hour and minute.</summary>
        public static void ParseTime(string value, out int hour, out int minute)
        {
            var v = (value ?? string.Empty).Trim();
            var parts = v.Split(':');
            minute = 0;
            if (parts.Length < 1 || parts.Length > 2 ||
                !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out hour) ||
                hour < 0 || hour > 23 ||
                (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out minute) || minute < 0 || minute > 59)))
            {
                throw new UsageException("Invalid time '" + value + "'. Use HH or HH:mm (24-hour), e.g. 03:00.");
            }
        }

        public static StartupMode ParseStartupMode(string value)
        {
            switch ((value ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "auto":
                case "automatic":
                    return StartupMode.Automatic;
                case "manual":
                case "demand":
                    return StartupMode.Manual;
                case "disabled":
                case "disable":
                    return StartupMode.Disabled;
                default:
                    throw new UsageException("Invalid startup mode '" + value + "'. Use: automatic, manual, disabled.");
            }
        }

        public static RebootMode ParseRebootMode(string value, RebootMode defaultMode)
        {
            if (value == null) return defaultMode;
            switch (value.Trim().ToLowerInvariant())
            {
                case "never":
                case "no":
                    return RebootMode.Never;
                case "if-required":
                case "ifrequired":
                case "auto":
                    return RebootMode.IfRequired;
                case "always":
                    return RebootMode.Always;
                default:
                    throw new UsageException("Invalid --reboot '" + value + "'. Use: never, if-required, always.");
            }
        }

        public static string RebootModeName(RebootMode mode)
        {
            switch (mode)
            {
                case RebootMode.IfRequired: return "if-required";
                case RebootMode.Always: return "always";
                default: return "never";
            }
        }

        public static void ValidateServerUrl(string url, string optionName)
        {
            Uri uri;
            if (Text.IsBlank(url) ||
                !Uri.TryCreate(url.Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != "http" && uri.Scheme != "https"))
            {
                throw new UsageException(optionName + " must be an http:// or https:// URL, e.g. http://wsus01:8530");
            }
        }
    }
}
