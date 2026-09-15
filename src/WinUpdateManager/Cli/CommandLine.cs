using System;
using System.Collections.Generic;
using System.Globalization;
using WinUpdateManager.Core;

namespace WinUpdateManager.Cli
{
    public sealed class ParsedArgs
    {
        private readonly Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public ParsedArgs()
        {
            Command = "menu";
            Positionals = new List<string>();
        }

        public string Command { get; set; }
        public bool CommandSpecified { get; set; }
        public List<string> Positionals { get; private set; }

        public IEnumerable<string> OptionNames
        {
            get { return options.Keys; }
        }

        public void Set(string name, string value)
        {
            options[name] = value;
        }

        public bool Has(string name)
        {
            return options.ContainsKey(name);
        }

        public string Get(string name, string defaultValue = null)
        {
            string value;
            return options.TryGetValue(name, out value) ? value : defaultValue;
        }

        public bool Flag(string name)
        {
            string value;
            if (!options.TryGetValue(name, out value)) return false;
            if (value == null) return true;
            switch (value.Trim().ToLowerInvariant())
            {
                case "":
                case "true":
                case "1":
                case "yes":
                case "on":
                    return true;
                default:
                    return false;
            }
        }

        public int GetInt(string name, int defaultValue, int min, int max)
        {
            string value = Get(name);
            if (value == null) return defaultValue;
            int n;
            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < min || n > max)
                throw new UsageException(string.Format(CultureInfo.InvariantCulture, "--{0} must be a whole number between {1} and {2}.", name, min, max));
            return n;
        }

        public string Positional(int index)
        {
            return index < Positionals.Count ? Positionals[index] : null;
        }
    }

    public static class CommandLineParser
    {
        public static readonly string[] Commands =
        {
            "menu", "status", "list", "download", "install", "uninstall", "hide", "unhide", "history",
            "pending-reboot", "reboot", "config", "services", "schedule", "microsoft-update", "help", "version"
        };

        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "info", "status" },
            { "search", "list" },
            { "scan", "list" },
            { "update", "install" },
            { "remove", "uninstall" },
            { "reboot-status", "pending-reboot" },
            { "reboot-pending", "pending-reboot" },
            { "restart", "reboot" },
            { "policy", "config" },
            { "service", "services" },
            { "task", "schedule" },
            { "mu", "microsoft-update" },
            { "interactive", "menu" },
        };

        private static readonly HashSet<string> FlagOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "json", "simulate", "quiet", "no-color", "no-log", "help", "yes", "dry-run", "include-drivers",
            "include-optional", "hidden", "installed", "all", "force", "no-accept-eula", "abort",
            "clear-wsus", "restart-service"
        };

        private static readonly HashSet<string> ValueOptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "log", "kb", "exclude-kb", "category", "severity", "title", "exclude-title", "id", "max-size-mb",
            "source", "criteria", "reboot", "reboot-delay", "count", "delay", "mode", "day", "time",
            "wsus-server", "wsus-status-server", "target-group", "name", "frequency", "at"
        };

        private static readonly string[] GlobalOptions = { "json", "simulate", "quiet", "no-color", "no-log", "log", "help", "yes" };
        private static readonly string[] FilterOptions = { "kb", "exclude-kb", "category", "severity", "title", "exclude-title", "id", "max-size-mb", "include-drivers", "include-optional", "source", "criteria" };

        private static readonly Dictionary<string, string[]> CommandOptions = new Dictionary<string, string[]>
        {
            { "menu", new string[0] },
            { "help", new string[0] },
            { "version", new string[0] },
            { "status", new string[0] },
            { "list", Combine(FilterOptions, "hidden", "installed") },
            { "download", Combine(FilterOptions, "dry-run", "no-accept-eula") },
            { "install", Combine(FilterOptions, "dry-run", "reboot", "reboot-delay", "no-accept-eula", "force") },
            { "uninstall", new[] { "kb", "id", "dry-run", "reboot", "reboot-delay", "source" } },
            { "hide", Combine(FilterOptions, "all", "dry-run") },
            { "unhide", Combine(FilterOptions, "all", "dry-run") },
            { "history", new[] { "count", "kb", "title" } },
            { "pending-reboot", new string[0] },
            { "reboot", new[] { "delay", "abort" } },
            { "config", new[] { "mode", "day", "time", "wsus-server", "wsus-status-server", "target-group", "clear-wsus", "restart-service" } },
            { "services", new string[0] },
            { "schedule", Combine(FilterOptions, "name", "frequency", "day", "at", "reboot", "reboot-delay") },
            { "microsoft-update", new string[0] },
        };

        public static ParsedArgs Parse(string[] args)
        {
            var result = new ParsedArgs();
            args = args ?? new string[0];

            for (int i = 0; i < args.Length; i++)
            {
                string token = args[i];
                if (string.IsNullOrEmpty(token)) continue;

                if (token == "/?" || token == "-?" || token == "-h")
                {
                    result.Set("help", "true");
                    continue;
                }
                if (token == "-y")
                {
                    result.Set("yes", "true");
                    continue;
                }
                if (token == "-q")
                {
                    result.Set("quiet", "true");
                    continue;
                }

                if (token.StartsWith("--", StringComparison.Ordinal))
                {
                    string name = token.Substring(2);
                    string value = null;
                    int eq = name.IndexOf('=');
                    if (eq >= 0)
                    {
                        value = name.Substring(eq + 1);
                        name = name.Substring(0, eq);
                    }
                    name = name.ToLowerInvariant();

                    if (name.Length == 0) throw new UsageException("Empty option name '--'.");

                    if (name == "version" && !result.CommandSpecified)
                    {
                        result.Command = "version";
                        result.CommandSpecified = true;
                        continue;
                    }

                    if (FlagOptions.Contains(name))
                    {
                        result.Set(name, value ?? "true");
                    }
                    else if (ValueOptions.Contains(name))
                    {
                        if (value == null)
                        {
                            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                                throw new UsageException("Option --" + name + " requires a value.");
                            value = args[++i];
                        }
                        result.Set(name, value);
                    }
                    else
                    {
                        throw new UsageException("Unknown option '--" + name + "'." + Suggest(name));
                    }
                    continue;
                }

                if (token.Length > 1 && token[0] == '-' && !char.IsDigit(token[1]))
                    throw new UsageException("Unknown option '" + token + "'. Options use two dashes, for example --json.");

                if (!result.CommandSpecified)
                {
                    result.Command = NormalizeCommand(token);
                    result.CommandSpecified = true;
                }
                else
                {
                    result.Positionals.Add(token);
                }
            }

            Validate(result);
            return result;
        }

        public static string NormalizeCommand(string token)
        {
            string name = token.Trim().ToLowerInvariant();
            string alias;
            if (Aliases.TryGetValue(name, out alias)) return alias;
            if (Array.IndexOf(Commands, name) >= 0) return name;
            throw new UsageException("Unknown command '" + token + "'. Run 'WinUpdateManager help' to see all commands.");
        }

        private static void Validate(ParsedArgs parsed)
        {
            string[] allowed;
            if (!CommandOptions.TryGetValue(parsed.Command, out allowed)) return;

            foreach (var name in parsed.OptionNames)
            {
                if (Array.IndexOf(GlobalOptions, name) >= 0) continue;
                if (Array.IndexOf(allowed, name) >= 0) continue;
                throw new UsageException("Option --" + name + " cannot be used with '" + parsed.Command +
                                         "'. Run 'WinUpdateManager help " + parsed.Command + "'.");
            }
        }

        private static string Suggest(string name)
        {
            string best = null;
            int bestDistance = int.MaxValue;
            foreach (var candidate in FlagOptions)
            {
                int d = Text.Levenshtein(name, candidate);
                if (d < bestDistance) { bestDistance = d; best = candidate; }
            }
            foreach (var candidate in ValueOptions)
            {
                int d = Text.Levenshtein(name, candidate);
                if (d < bestDistance) { bestDistance = d; best = candidate; }
            }
            return best != null && bestDistance <= 2 ? " Did you mean --" + best + "?" : string.Empty;
        }

        private static string[] Combine(string[] first, params string[] extra)
        {
            var list = new List<string>(first);
            list.AddRange(extra);
            return list.ToArray();
        }
    }
}
