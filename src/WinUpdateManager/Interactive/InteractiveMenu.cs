using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using WinUpdateManager.Cli;
using WinUpdateManager.Core;
using WinUpdateManager.Output;

namespace WinUpdateManager.Interactive
{
    /// <summary>Menu driven user interface; works in any console including Server Core.</summary>
    public sealed class InteractiveMenu
    {
        private static readonly string[] SubMenus = { "7", "9", "10", "11", "12", "13" };

        private readonly App app;
        private readonly AppServices s;
        private SystemInfo sys;

        public InteractiveMenu(App app)
        {
            this.app = app;
            s = app.Services;
        }

        private sealed class MenuItem
        {
            public MenuItem(string key, string label, Action action)
            {
                Key = key;
                Label = label;
                Action = action;
            }

            public string Key { get; private set; }
            public string Label { get; private set; }
            public Action Action { get; private set; }
        }

        public int Run()
        {
            ConsoleUi.Quiet = false;
            try
            {
                sys = s.Environment.GetSystemInfo();
            }
            catch (Exception ex)
            {
                ConsoleUi.Log.Warn("Could not read system information: " + ex.Message);
            }

            while (true)
            {
                DrawHeader();
                DrawMainMenu();
                string choice = ConsoleUi.Prompt("Select an option:");
                if (choice == null) return ExitCodes.Success;
                choice = choice.Trim().ToLowerInvariant();
                if (choice.Length == 0) continue;
                if (choice == "0" || choice == "q" || choice == "quit" || choice == "exit") return ExitCodes.Success;

                if (choice == "e" && !s.Environment.IsAdministrator)
                {
                    if (s.Environment.RelaunchElevated("menu"))
                    {
                        ConsoleUi.Info("An elevated window was opened. This window will close.");
                        return ExitCodes.Success;
                    }
                    ConsoleUi.Warn("Could not start an elevated process (cancelled or not supported).");
                }
                else
                {
                    bool known = true;
                    app.Guard(() => known = Dispatch(choice));
                    if (!known) ConsoleUi.Warn("Unknown option '" + choice + "'.");
                    else if (Array.IndexOf(SubMenus, choice) >= 0) continue; // submenus pause on their own
                }

                if (!ConsoleUi.Pause()) return ExitCodes.Success;
            }
        }

        private void DrawHeader()
        {
            string line = new string('=', Math.Min(ConsoleUi.Width - 1, 86));
            bool pending = false;
            try
            {
                pending = app.CollectRebootReasons().Any(r => r.Definite);
            }
            catch (Exception ex)
            {
                ConsoleUi.Log.Warn("Reboot check failed: " + ex.Message);
            }

            ConsoleUi.Line();
            ConsoleUi.Colored(line, ConsoleColor.Cyan);
            ConsoleUi.Colored(" " + App.ExeName + " " + App.Version + "  -  Windows Server Update Manager", ConsoleColor.White);
            if (sys != null)
            {
                ConsoleUi.Line(" Host: " + sys.ComputerName + "  |  " + sys.Family + " (build " + sys.BuildDisplay + ")" +
                               (sys.IsServerCore ? "  |  Server Core" : string.Empty));
            }
            ConsoleUi.Line(" Administrator: " + Text.YesNo(s.Environment.IsAdministrator) + "  |  Restart pending: " + (pending ? "YES" : "No"));
            if (s.IsSimulation)
                ConsoleUi.Colored(" SIMULATION MODE - a virtual server is used, nothing on this computer is changed", ConsoleColor.Magenta);
            if (!s.Environment.IsAdministrator)
                ConsoleUi.Colored(" Not elevated: changes need 'Run as administrator' (press E to relaunch elevated)", ConsoleColor.Yellow);
            ConsoleUi.Colored(line, ConsoleColor.Cyan);
        }

        private static void DrawMainMenu()
        {
            ConsoleUi.Line("   1) Show system and Windows Update status");
            ConsoleUi.Line("   2) Scan for available updates");
            ConsoleUi.Line("   3) Install ALL available updates");
            ConsoleUi.Line("   4) Choose which updates to install");
            ConsoleUi.Line("   5) Download updates only");
            ConsoleUi.Line("   6) View update history");
            ConsoleUi.Line("   7) Hide / unhide updates");
            ConsoleUi.Line("   8) Uninstall an update");
            ConsoleUi.Line("   9) Automatic update policy and WSUS settings");
            ConsoleUi.Line("  10) Windows Update services and repair");
            ConsoleUi.Line("  11) Scheduled automatic installation");
            ConsoleUi.Line("  12) Microsoft Update (SQL Server, .NET and other products)");
            ConsoleUi.Line("  13) Restart: check pending / restart server");
            ConsoleUi.Line("   H) Command line help");
            ConsoleUi.Line("   0) Exit");
            ConsoleUi.Line();
        }

        private bool Dispatch(string choice)
        {
            switch (choice)
            {
                case "1": app.ShowStatus(); return true;
                case "2": ScanOnly(); return true;
                case "3": InstallAll(false); return true;
                case "4": InstallSelected(); return true;
                case "5": InstallAll(true); return true;
                case "6": History(); return true;
                case "7": HideMenu(); return true;
                case "8": Uninstall(); return true;
                case "9": PolicyMenu(); return true;
                case "10": ServicesMenu(); return true;
                case "11": ScheduleMenu(); return true;
                case "12": MicrosoftUpdateMenu(); return true;
                case "13": RestartMenu(); return true;
                case "h":
                case "?":
                    ConsoleUi.Raw(HelpText.General);
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------ update actions

        private void ScanOnly()
        {
            bool include = AskYesNo("Include optional updates and drivers?");
            var updates = app.Scan(new SearchOptions { IncludeDrivers = include }, new UpdateFilter { IncludeOptional = include, IncludeDrivers = include });
            if (updates.Count == 0)
            {
                ConsoleUi.Success("No updates available - the server is up to date.");
                return;
            }
            app.PrintUpdateTable(updates);
        }

        private void InstallAll(bool downloadOnly)
        {
            app.RequireAdmin();
            var updates = app.Scan(new SearchOptions(), new UpdateFilter());
            if (updates.Count > 0) app.PrintUpdateTable(updates);
            var outcome = app.ProcessUpdates(updates, new ProcessSettings { DownloadOnly = downloadOnly });
            if (!downloadOnly) OfferRestart(outcome);
        }

        private void InstallSelected()
        {
            app.RequireAdmin();
            var updates = app.Scan(new SearchOptions { IncludeDrivers = true }, new UpdateFilter { IncludeOptional = true, IncludeDrivers = true });
            if (updates.Count == 0)
            {
                ConsoleUi.Success("No updates available - the server is up to date.");
                return;
            }
            app.PrintUpdateTable(updates);
            var selected = PromptSelection(updates);
            if (selected == null) return;
            var outcome = app.ProcessUpdates(selected, new ProcessSettings());
            OfferRestart(outcome);
        }

        private void OfferRestart(ProcessOutcome outcome)
        {
            if (!outcome.RebootRequired || outcome.RestartScheduled) return;
            if (AskYesNo("Restart the server now to finish the installation?"))
            {
                int delay = AskInt("Seconds before restart", 60, 0, 86400);
                app.ScheduleRestart(delay, true);
            }
        }

        private void History()
        {
            int count = AskInt("How many history entries", 30, 1, 5000);
            app.ShowHistory(count, new List<string>(), null);
        }

        private void Uninstall()
        {
            app.RequireAdmin();
            string kb = ConsoleUi.Prompt("KB number to uninstall (e.g. KB5030216), Enter to cancel:");
            if (Text.IsBlank(kb)) return;
            var args = new ParsedArgs();
            args.Set("kb", kb.Trim());
            var filter = UpdateFilter.FromArgs(args);
            var updates = app.Scan(new SearchOptions { Scope = SearchScope.Installed, IncludeDrivers = true }, filter);
            var outcome = app.UninstallUpdates(updates, new ProcessSettings());
            OfferRestart(outcome);
        }

        private void HideMenu()
        {
            SubMenu("Hide / unhide updates", null,
                new MenuItem("1", "Hide updates (they will not be installed)", () =>
                {
                    app.RequireAdmin();
                    var updates = app.Scan(new SearchOptions { IncludeDrivers = true }, new UpdateFilter { IncludeOptional = true, IncludeDrivers = true });
                    if (updates.Count == 0)
                    {
                        ConsoleUi.Info("No visible updates to hide.");
                        return;
                    }
                    app.PrintUpdateTable(updates);
                    var selected = PromptSelection(updates);
                    if (selected != null) app.HideUpdates(selected, true, false);
                }),
                new MenuItem("2", "Unhide updates", () =>
                {
                    app.RequireAdmin();
                    var updates = app.Scan(new SearchOptions { Scope = SearchScope.Hidden, IncludeDrivers = true }, new UpdateFilter { IncludeOptional = true, IncludeDrivers = true });
                    if (updates.Count == 0)
                    {
                        ConsoleUi.Info("There are no hidden updates.");
                        return;
                    }
                    app.PrintUpdateTable(updates);
                    var selected = PromptSelection(updates);
                    if (selected != null) app.HideUpdates(selected, false, false);
                }));
        }

        // ------------------------------------------------------------ configuration

        private void PolicyMenu()
        {
            SubMenu("Automatic update policy and WSUS", app.ShowPolicy,
                new MenuItem("1", "Set automatic update mode", () =>
                {
                    ConsoleUi.Line("     1 = disabled, 2 = notify, 3 = auto download + notify install,");
                    ConsoleUi.Line("     4 = auto download + scheduled install, 5 = local admin chooses");
                    string mode = ConsoleUi.Prompt("Mode (1-5):");
                    if (Text.IsBlank(mode)) return;
                    ApplyPolicy(new PolicyChange { Mode = PolicyParsing.ParseMode(mode) });
                }),
                new MenuItem("2", "Set scheduled install day and time", () =>
                {
                    string day = ConsoleUi.Prompt("Day (every, sunday..saturday) [every]:");
                    int hour = AskInt("Hour (0-23)", 3, 0, 23);
                    ApplyPolicy(new PolicyChange { Day = PolicyParsing.ParseDay(Text.IsBlank(day) ? "every" : day), Hour = hour });
                }),
                new MenuItem("3", "Use a WSUS server", () =>
                {
                    string url = ConsoleUi.Prompt("WSUS server URL (e.g. http://wsus01:8530):");
                    if (Text.IsBlank(url)) return;
                    PolicyParsing.ValidateServerUrl(url, "WSUS server");
                    ApplyPolicy(new PolicyChange { WsusServer = url.Trim().TrimEnd('/') });
                }),
                new MenuItem("4", "Stop using WSUS (use Windows Update)", () => ApplyPolicy(new PolicyChange { ClearWsus = true })),
                new MenuItem("5", "Set WSUS target group", () =>
                {
                    string group = ConsoleUi.Prompt("Target group name (empty to clear):");
                    if (group == null) return;
                    ApplyPolicy(new PolicyChange { TargetGroup = group.Trim() });
                }),
                new MenuItem("6", "Reset all Windows Update policy values", () =>
                {
                    if (AskYesNo("Remove automatic update mode, schedule and WSUS settings?"))
                        ApplyPolicy(new PolicyChange { ResetAll = true });
                }));
        }

        private void ApplyPolicy(PolicyChange change)
        {
            app.RequireAdmin();
            bool restart = AskYesNo("Restart the Windows Update service so the change applies now?");
            app.ApplyPolicy(change, restart);
        }

        private void ServicesMenu()
        {
            SubMenu("Windows Update services", app.ShowServices,
                new MenuItem("1", "Restart Windows Update service (wuauserv)", () => app.RestartService("wuauserv")),
                new MenuItem("2", "Restart BITS", () => app.RestartService("bits")),
                new MenuItem("3", "Restart Cryptographic Services", () => app.RestartService("cryptsvc")),
                new MenuItem("4", "Enable Windows Update service (startup = Manual)", () =>
                {
                    app.RequireAdmin();
                    s.Services.SetStartupMode("wuauserv", StartupMode.Manual);
                    ConsoleUi.Success("wuauserv startup type set to Manual (trigger start).");
                }),
                new MenuItem("5", "Repair: reset Windows Update components", () => app.ResetComponents(false)));
        }

        private void ScheduleMenu()
        {
            SubMenu("Scheduled automatic installation", () => app.ShowSchedule(ScheduleDefinition.DefaultTaskName),
                new MenuItem("1", "Create or update the scheduled task", () =>
                {
                    app.RequireAdmin();
                    var def = new ScheduleDefinition();
                    string freq = (ConsoleUi.Prompt("Frequency (daily/weekly) [weekly]:") ?? string.Empty).Trim().ToLowerInvariant();
                    def.Frequency = freq == "daily" ? ScheduleFrequency.Daily : ScheduleFrequency.Weekly;
                    if (def.Frequency == ScheduleFrequency.Weekly)
                    {
                        string day = ConsoleUi.Prompt("Day (sunday..saturday) [sunday]:");
                        def.Day = PolicyParsing.ToDayOfWeek(PolicyParsing.ParseDay(Text.IsBlank(day) ? "sunday" : day));
                    }
                    string at = ConsoleUi.Prompt("Time HH:mm [03:00]:");
                    int hour, minute;
                    PolicyParsing.ParseTime(Text.IsBlank(at) ? "03:00" : at, out hour, out minute);
                    def.Hour = hour;
                    def.Minute = minute;

                    string rebootText = ConsoleUi.Prompt("Restart after install (never/if-required/always) [if-required]:");
                    var reboot = PolicyParsing.ParseRebootMode(Text.IsBlank(rebootText) ? null : rebootText, RebootMode.IfRequired);

                    var args = new ParsedArgs();
                    if (AskYesNo("Only install Security and Critical updates?")) args.Set("category", "Security,Critical");
                    if (AskYesNo("Skip preview updates?")) args.Set("exclude-title", "Preview");
                    def.Arguments = App.BuildScheduledArguments(args, reboot, 300);
                    app.CreateSchedule(def);
                }),
                new MenuItem("2", "Delete the scheduled task", () =>
                {
                    app.RequireAdmin();
                    if (s.Scheduler.Delete(ScheduleDefinition.DefaultTaskName)) ConsoleUi.Success("Scheduled task deleted.");
                    else ConsoleUi.Info("The scheduled task does not exist.");
                }));
        }

        private void MicrosoftUpdateMenu()
        {
            SubMenu("Microsoft Update", app.ShowUpdateServices,
                new MenuItem("1", "Enable Microsoft Update", () => app.SetMicrosoftUpdate(true)),
                new MenuItem("2", "Disable Microsoft Update", () => app.SetMicrosoftUpdate(false)));
        }

        private void RestartMenu()
        {
            SubMenu("Restart", () => app.ShowPendingReboot(),
                new MenuItem("1", "Restart the server", () =>
                {
                    int delay = AskInt("Seconds before restart", 60, 0, 86400);
                    app.ScheduleRestart(delay, false);
                }),
                new MenuItem("2", "Cancel a scheduled restart", () =>
                {
                    app.RequireAdmin();
                    s.Power.AbortRestart();
                    ConsoleUi.Success("Scheduled restart cancelled.");
                }));
        }

        // ------------------------------------------------------------ helpers

        private void SubMenu(string title, Action show, params MenuItem[] items)
        {
            while (true)
            {
                if (show != null) app.Guard(show);
                ConsoleUi.Header(title);
                foreach (var item in items) ConsoleUi.Line("   " + item.Key + ") " + item.Label);
                ConsoleUi.Line("   0) Back");
                ConsoleUi.Line();
                string choice = ConsoleUi.Prompt("Select an option:");
                if (choice == null) return;
                choice = choice.Trim();
                if (choice.Length == 0 || choice == "0") return;

                var selected = items.FirstOrDefault(i => i.Key == choice);
                if (selected == null)
                {
                    ConsoleUi.Warn("Unknown option '" + choice + "'.");
                    continue;
                }
                app.Guard(selected.Action);
                if (!ConsoleUi.Pause()) return;
            }
        }

        private static List<UpdateInfo> PromptSelection(IList<UpdateInfo> updates)
        {
            while (true)
            {
                string input = ConsoleUi.Prompt("Enter numbers (e.g. 1,3,5-7) or 'all'; press Enter to cancel:");
                if (Text.IsBlank(input)) return null;
                try
                {
                    return SelectionParser.Parse(input, updates.Count).Select(i => updates[i - 1]).ToList();
                }
                catch (FormatException ex)
                {
                    ConsoleUi.Warn(ex.Message);
                }
            }
        }

        private static bool AskYesNo(string question)
        {
            return ConsoleUi.Confirm(question, false) == true;
        }

        private static int AskInt(string question, int defaultValue, int min, int max)
        {
            while (true)
            {
                string input = ConsoleUi.Prompt(question + " [" + defaultValue + "]:");
                if (Text.IsBlank(input)) return defaultValue;
                int n;
                if (int.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) && n >= min && n <= max) return n;
                ConsoleUi.Warn("Enter a number between " + min + " and " + max + ".");
            }
        }
    }
}
