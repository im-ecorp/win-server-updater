using System;
using System.Collections.Generic;
using System.Globalization;
using WinUpdateManager.Cli;

namespace WinUpdateManager.Core
{
    /// <summary>
    /// Client-side selection of updates returned by a search (KB numbers, classification, severity, title...).
    /// </summary>
    public sealed class UpdateFilter
    {
        public UpdateFilter()
        {
            Kbs = new List<string>();
            ExcludeKbs = new List<string>();
            Classifications = new List<string>();
            Severities = new List<string>();
            TitleContains = new List<string>();
            TitleExcludes = new List<string>();
            Ids = new List<string>();
        }

        public List<string> Kbs { get; private set; }
        public List<string> ExcludeKbs { get; private set; }
        public List<string> Classifications { get; private set; }
        public List<string> Severities { get; private set; }
        public List<string> TitleContains { get; private set; }
        public List<string> TitleExcludes { get; private set; }
        public List<string> Ids { get; private set; }
        public bool IncludeOptional { get; set; }
        public bool IncludeDrivers { get; set; }
        public decimal? MaxSizeMb { get; set; }

        /// <summary>True when specific updates are requested by KB number or update ID.</summary>
        public bool HasExplicitSelection
        {
            get { return Kbs.Count > 0 || Ids.Count > 0; }
        }

        /// <summary>True when any narrowing criterion (besides optional/driver switches) is set.</summary>
        public bool HasCriteria
        {
            get
            {
                return HasExplicitSelection || ExcludeKbs.Count > 0 || Classifications.Count > 0 ||
                       Severities.Count > 0 || TitleContains.Count > 0 || TitleExcludes.Count > 0 ||
                       MaxSizeMb.HasValue;
            }
        }

        public static UpdateFilter FromArgs(ParsedArgs args)
        {
            var f = new UpdateFilter();
            foreach (var kb in Text.SplitList(args.Get("kb")))
            {
                var n = Text.NormalizeKb(kb);
                if (!IsDigits(n)) throw new UsageException("Invalid KB number '" + kb + "'. Example: --kb KB5030216,5031364");
                f.Kbs.Add(n);
            }
            foreach (var kb in Text.SplitList(args.Get("exclude-kb")))
            {
                var n = Text.NormalizeKb(kb);
                if (!IsDigits(n)) throw new UsageException("Invalid KB number '" + kb + "' in --exclude-kb.");
                f.ExcludeKbs.Add(n);
            }
            f.Classifications.AddRange(Text.SplitList(args.Get("category")));
            f.Severities.AddRange(Text.SplitList(args.Get("severity")));
            f.TitleContains.AddRange(Text.SplitList(args.Get("title")));
            f.TitleExcludes.AddRange(Text.SplitList(args.Get("exclude-title")));
            f.Ids.AddRange(Text.SplitList(args.Get("id")));
            f.IncludeOptional = args.Flag("include-optional");
            f.IncludeDrivers = args.Flag("include-drivers");

            var max = args.Get("max-size-mb");
            if (max != null)
            {
                decimal mb;
                if (!decimal.TryParse(max, NumberStyles.Number, CultureInfo.InvariantCulture, out mb) || mb <= 0)
                    throw new UsageException("--max-size-mb must be a positive number.");
                f.MaxSizeMb = mb;
            }
            return f;
        }

        public IList<UpdateInfo> Apply(IEnumerable<UpdateInfo> updates)
        {
            var result = new List<UpdateInfo>();
            foreach (var u in updates)
            {
                if (Matches(u)) result.Add(u);
            }
            return result;
        }

        public bool Matches(UpdateInfo u)
        {
            bool explicitlySelected = false;

            if (Ids.Count > 0)
            {
                if (!Ids.Exists(id => Text.EqualsIgnoreCase(id, u.Id))) return false;
                explicitlySelected = true;
            }

            if (Kbs.Count > 0)
            {
                if (!u.KbArticleIds.Exists(k => Kbs.Contains(Text.NormalizeKb(k)))) return false;
                explicitlySelected = true;
            }

            if (ExcludeKbs.Count > 0 && u.KbArticleIds.Exists(k => ExcludeKbs.Contains(Text.NormalizeKb(k))))
                return false;

            if (!explicitlySelected)
            {
                if (u.IsOptional && !IncludeOptional) return false;
                if (u.Kind == UpdateKind.Driver && !IncludeDrivers) return false;
            }

            if (Classifications.Count > 0)
            {
                bool match = false;
                foreach (var wanted in Classifications)
                {
                    if (u.Classifications.Exists(c => Text.ContainsIgnoreCase(c, wanted)) ||
                        (u.Kind == UpdateKind.Driver && Text.ContainsIgnoreCase("Drivers", wanted)))
                    {
                        match = true;
                        break;
                    }
                }
                if (!match) return false;
            }

            if (Severities.Count > 0)
            {
                string sev = Text.IsBlank(u.Severity) ? "Unspecified" : u.Severity;
                if (!Severities.Exists(s => Text.EqualsIgnoreCase(s, sev))) return false;
            }

            if (TitleContains.Count > 0 && !TitleContains.Exists(t => Text.ContainsIgnoreCase(u.Title, t)))
                return false;

            if (TitleExcludes.Count > 0 && TitleExcludes.Exists(t => Text.ContainsIgnoreCase(u.Title, t)))
                return false;

            if (MaxSizeMb.HasValue && u.MaxDownloadSize > MaxSizeMb.Value * 1024m * 1024m)
                return false;

            return true;
        }

        public string Describe()
        {
            var parts = new List<string>();
            if (Kbs.Count > 0) parts.Add("KB in [" + Text.Join(", ", Kbs) + "]");
            if (Ids.Count > 0) parts.Add("ID in [" + Text.Join(", ", Ids) + "]");
            if (ExcludeKbs.Count > 0) parts.Add("excluding KB [" + Text.Join(", ", ExcludeKbs) + "]");
            if (Classifications.Count > 0) parts.Add("classification ~ [" + Text.Join(", ", Classifications) + "]");
            if (Severities.Count > 0) parts.Add("severity in [" + Text.Join(", ", Severities) + "]");
            if (TitleContains.Count > 0) parts.Add("title contains [" + Text.Join(", ", TitleContains) + "]");
            if (TitleExcludes.Count > 0) parts.Add("title excludes [" + Text.Join(", ", TitleExcludes) + "]");
            if (MaxSizeMb.HasValue) parts.Add("size <= " + MaxSizeMb.Value.ToString(CultureInfo.InvariantCulture) + " MB");
            parts.Add(IncludeOptional ? "optional included" : "optional excluded");
            parts.Add(IncludeDrivers ? "drivers included" : "drivers excluded");
            return Text.Join("; ", parts);
        }

        private static bool IsDigits(string s)
        {
            if (s.Length == 0) return false;
            foreach (char c in s)
            {
                if (c < '0' || c > '9') return false;
            }
            return true;
        }
    }

    public static class SearchCriteria
    {
        public static string Build(SearchOptions options)
        {
            if (!Text.IsBlank(options.RawCriteria)) return options.RawCriteria.Trim();

            string criteria;
            switch (options.Scope)
            {
                case SearchScope.Hidden:
                    criteria = "IsInstalled=0 and IsHidden=1";
                    break;
                case SearchScope.Installed:
                    criteria = "IsInstalled=1";
                    break;
                default:
                    criteria = "IsInstalled=0 and IsHidden=0";
                    break;
            }
            if (!options.IncludeDrivers) criteria += " and Type='Software'";
            return criteria;
        }

        public static UpdateSource ParseSource(string value)
        {
            if (Text.IsBlank(value)) return UpdateSource.Default;
            switch (value.Trim().ToLowerInvariant())
            {
                case "default":
                case "auto":
                    return UpdateSource.Default;
                case "wsus":
                case "managed":
                    return UpdateSource.Wsus;
                case "wu":
                case "windowsupdate":
                case "windows-update":
                    return UpdateSource.WindowsUpdate;
                case "mu":
                case "microsoftupdate":
                case "microsoft-update":
                    return UpdateSource.MicrosoftUpdate;
                default:
                    throw new UsageException("Invalid --source '" + value + "'. Use: default, wsus, windowsupdate, microsoftupdate.");
            }
        }

        public static string DescribeSource(UpdateSource source)
        {
            switch (source)
            {
                case UpdateSource.Wsus: return "WSUS (managed server)";
                case UpdateSource.WindowsUpdate: return "Windows Update (internet)";
                case UpdateSource.MicrosoftUpdate: return "Microsoft Update (internet)";
                default: return "the configured update source";
            }
        }
    }
}
