using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using WinUpdateManager.Cli;
using WinUpdateManager.Core;
using WinUpdateManager.Interactive;
using WinUpdateManager.Output;

namespace WinUpdateManager
{
    public sealed class AppServices
    {
        public IUpdateProvider Updates { get; set; }
        public ISystemEnvironment Environment { get; set; }
        public IPolicyStore Policy { get; set; }
        public IServiceControl Services { get; set; }
        public ITaskSchedulerService Scheduler { get; set; }
        public IPowerControl Power { get; set; }
        public bool IsSimulation { get; set; }
    }

    public static class ExitCodes
    {
        public const int Success = 0;
        public const int Error = 1;
        public const int Usage = 2;
        public const int PartialFailure = 4;
        public const int NotElevated = 5;
        public const int RebootRequired = 3010;
    }

    public sealed class ProcessSettings
    {
        public ProcessSettings()
        {
            AcceptEula = true;
            Reboot = RebootMode.Never;
            RebootDelaySeconds = 120;
        }

        public bool DownloadOnly { get; set; }
        public bool AcceptEula { get; set; }
        public bool DryRun { get; set; }
        public bool AssumeYes { get; set; }
        public bool Force { get; set; }
        public RebootMode Reboot { get; set; }
        public int RebootDelaySeconds { get; set; }
    }

    public sealed class ProcessOutcome
    {
        public ProcessOutcome()
        {
            Results = new List<UpdateActionResult>();
        }

        public List<UpdateActionResult> Results { get; private set; }
        public bool RebootRequired { get; set; }
        public bool RestartScheduled { get; set; }
        public bool Cancelled { get; set; }
        public bool NoInput { get; set; }
        public bool Aborted { get; set; }

        public int Succeeded
        {
            get { return Results.Count(r => r.IsSuccess); }
        }

        public int Failed
        {
            get { return Results.Count(r => r.Result == OperationResult.Failed || r.Result == OperationResult.Aborted); }
        }

        public int ExitCode
        {
            get
            {
                if (Failed > 0) return Succeeded > 0 ? ExitCodes.PartialFailure : ExitCodes.Error;
                if (Aborted || NoInput) return ExitCodes.Error;
                if (RebootRequired && !RestartScheduled) return ExitCodes.RebootRequired;
                return ExitCodes.Success;
            }
        }
    }

    /// <summary>Command implementations shared by the command line and the interactive menu.</summary>
    public sealed class App
    {
        public const string Version = "1.0.0";
        public const string ExeName = "WinUpdateManager";

        private readonly AppServices s;
        private volatile bool cancelRequested;
        private bool json;

        public App(AppServices services)
        {
            s = services;
        }

        public AppServices Services
        {
            get { return s; }
        }

        public bool CancelRequested
        {
            get { return cancelRequested; }
        }

        public void RequestCancel()
        {
            cancelRequested = true;
        }

        // ------------------------------------------------------------------ dispatch

        public int Run(ParsedArgs a)
        {
            json = a.Flag("json");
            ConsoleUi.Quiet = json || a.Flag("quiet");

            if (a.Flag("help") && a.Command != "help")
            {
                ConsoleUi.Raw(HelpText.For(a.Command));
                return ExitCodes.Success;
            }

            ConsoleUi.Log.Info("=== " + ExeName + " " + Version + " | command: " + a.Command +
                               (a.Positionals.Count > 0 ? " " + Text.Join(" ", a.Positionals) : string.Empty) +
                               " | options: " + Text.Join(" ", a.OptionNames.Select(n => "--" + n)) +
                               (s.IsSimulation ? " | SIMULATION" : string.Empty));

            try
            {
                switch (a.Command)
                {
                    case "menu":
                        json = false;
                        ConsoleUi.Quiet = false;
                        return new InteractiveMenu(this).Run();
                    case "help":
                        ConsoleUi.Raw(HelpText.For(a.Positional(0) == null ? null : CommandLineParser.NormalizeCommand(a.Positional(0))));
                        return ExitCodes.Success;
                    case "version":
                        ConsoleUi.Raw(ExeName + " " + Version + " (.NET CLR " + System.Environment.Version + ")");
                        return ExitCodes.Success;
                    case "status": return CmdStatus();
                    case "list": return CmdList(a);
                    case "download": return CmdProcess(a, true);
                    case "install": return CmdProcess(a, false);
                    case "uninstall": return CmdUninstall(a);
                    case "hide": return CmdHide(a, true);
                    case "unhide": return CmdHide(a, false);
                    case "history": return CmdHistory(a);
                    case "pending-reboot": return ShowPendingReboot() ? ExitCodes.RebootRequired : ExitCodes.Success;
                    case "reboot": return CmdReboot(a);
                    case "config": return CmdConfig(a);
                    case "services": return CmdServices(a);
                    case "schedule": return CmdSchedule(a);
                    case "microsoft-update": return CmdMicrosoftUpdate(a);
                    default: throw new UsageException("Unknown command '" + a.Command + "'.");
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

        public int HandleException(Exception ex)
        {
            if (ex is UsageException)
            {
                ConsoleUi.Error(ex.Message);
                return ExitCodes.Usage;
            }
            if (ex is NotElevatedException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
            {
                ConsoleUi.Error(ex.Message);
                return ExitCodes.NotElevated;
            }
            var api = ex as ApiException;
            if (api != null)
            {
                ConsoleUi.Error(api.Message + (api.ErrorCode != 0 ? " [" + WuaErrors.Format(api.ErrorCode) + "]" : string.Empty));
                if (api.ErrorCode == unchecked((int)0x80070005)) return ExitCodes.NotElevated;
                return ExitCodes.Error;
            }
            ConsoleUi.Error(ex.Message);
            ConsoleUi.Log.Error(ex.ToString());
            return ExitCodes.Error;
        }

        /// <summary>Runs an action, printing errors instead of throwing (used by the menu).</summary>
        public int Guard(Action action)
        {
            try
            {
                action();
                return ExitCodes.Success;
            }
            catch (Exception ex)
            {
                return HandleException(ex);
            }
        }

        public void RequireAdmin()
        {
            if (!s.Environment.IsAdministrator) throw new NotElevatedException();
        }

        // ------------------------------------------------------------------ status

        private int CmdStatus()
        {
            ShowStatus();
            return ExitCodes.Success;
        }

        public void ShowStatus()
        {
            var sys = s.Environment.GetSystemInfo();
            var reboot = CollectRebootReasons();
            var policy = s.Policy.Read();

            AgentInfo agent = null;
            string agentError = null;
            try
            {
                agent = s.Updates.GetAgentInfo();
            }
            catch (Exception ex)
            {
                agentError = ex.Message;
            }

            IList<UpdateServiceInfo> updateServices = new List<UpdateServiceInfo>();
            try
            {
                updateServices = s.Updates.GetUpdateServices();
            }
            catch (Exception ex)
            {
                ConsoleUi.Log.Warn("Could not read update services: " + ex.Message);
            }

            bool? busy = null;
            try
            {
                busy = s.Updates.IsInstallerBusy();
            }
            catch (Exception ex)
            {
                ConsoleUi.Log.Warn("Could not read installer state: " + ex.Message);
            }

            IList<ServiceState> services = new List<ServiceState>();
            try
            {
                services = s.Services.GetUpdateServices();
            }
            catch (Exception ex)
            {
                ConsoleUi.Log.Warn("Could not read services: " + ex.Message);
            }

            bool muRegistered = updateServices.Any(x => Text.EqualsIgnoreCase(x.ServiceId, WellKnownServices.MicrosoftUpdate));
            string source = DescribeEffectiveSource(policy, updateServices);

            if (json)
            {
                var w = new JsonWriter().BeginObject();
                w.BeginObject("system")
                    .Property("computerName", sys.ComputerName)
                    .Property("fqdn", sys.Fqdn)
                    .Property("productName", sys.ProductName)
                    .Property("family", sys.Family)
                    .Property("edition", sys.EditionId)
                    .Property("installationType", sys.InstallationType)
                    .Property("displayVersion", sys.DisplayVersion)
                    .Property("build", sys.BuildDisplay)
                    .Property("isServer", sys.IsServer)
                    .Property("isServerCore", sys.IsServerCore)
                    .Property("isDomainController", sys.IsDomainController)
                    .Property("isDomainJoined", sys.IsDomainJoined)
                    .Property("architecture", sys.Architecture)
                    .Property("clrVersion", sys.ClrVersion)
                    .Property("lastBoot", sys.LastBoot)
                    .Property("extendedSupportEnd", sys.ExtendedSupportEnd)
                    .Property("isElevated", sys.IsElevated)
                    .EndObject();
                w.BeginObject("windowsUpdate")
                    .Property("agentVersion", agent != null ? agent.Version : null)
                    .Property("agentError", agentError)
                    .Property("effectiveSource", source)
                    .Property("automaticUpdatesPolicy", policy.DescribeMode())
                    .Property("wsusServer", policy.WUServer)
                    .Property("wsusStatusServer", policy.WUStatusServer)
                    .Property("targetGroup", policy.TargetGroup)
                    .Property("microsoftUpdateRegistered", muRegistered)
                    .Property("lastSearchSuccess", agent != null ? agent.LastSearchSuccess : null)
                    .Property("lastInstallSuccess", agent != null ? agent.LastInstallSuccess : null)
                    .Property("installerBusy", busy)
                    .EndObject();
                w.BeginArray("services");
                foreach (var svc in services)
                {
                    w.BeginObject().Property("name", svc.Name).Property("status", svc.Status).Property("startMode", svc.StartMode).EndObject();
                }
                w.EndArray();
                WriteRebootJson(w, reboot);
                ConsoleUi.Raw(w.EndObject().ToString());
                return;
            }

            ConsoleUi.Header("System");
            ConsoleUi.KeyValue("Computer", sys.ComputerName + (Text.IsBlank(sys.Fqdn) ? string.Empty : " (" + sys.Fqdn + ")"));
            ConsoleUi.KeyValue("Operating system", sys.ProductName);
            ConsoleUi.KeyValue("Version", sys.Family + " | build " + sys.BuildDisplay +
                                          (Text.IsBlank(sys.DisplayVersion) ? string.Empty : " | " + sys.DisplayVersion) +
                                          (Text.IsBlank(sys.ServicePack) ? string.Empty : " | " + sys.ServicePack));
            ConsoleUi.KeyValue("Installation type", Text.IsBlank(sys.InstallationType) ? "-" : sys.InstallationType);
            ConsoleUi.KeyValue("Role", sys.IsDomainController ? "Domain controller" : sys.IsServer ? (sys.IsDomainJoined ? "Member server" : "Standalone server") : "Client operating system");
            ConsoleUi.KeyValue("Architecture", sys.Architecture);
            if (sys.LastBoot.HasValue)
                ConsoleUi.KeyValue("Last boot", Text.FormatDate(sys.LastBoot) + " (up " + Text.FormatDuration(DateTime.Now - sys.LastBoot.Value) + ")");
            if (sys.ExtendedSupportEnd.HasValue)
            {
                bool ended = sys.ExtendedSupportEnd.Value < DateTime.Now;
                ConsoleUi.KeyValue("Support lifecycle",
                    ended
                        ? "Extended support ended " + sys.ExtendedSupportEnd.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " (updates only with ESU)"
                        : "Extended support until " + sys.ExtendedSupportEnd.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ended ? ConsoleColor.Yellow : (ConsoleColor?)null);
            }
            ConsoleUi.KeyValue(".NET runtime", sys.ClrVersion);
            ConsoleUi.KeyValue("Elevated (admin)", Text.YesNo(sys.IsElevated), sys.IsElevated ? (ConsoleColor?)null : ConsoleColor.Yellow);
            if (!sys.IsServer) ConsoleUi.Warn("This is not a Windows Server operating system. The tool still works, but it is designed for servers.");

            ConsoleUi.Header("Windows Update");
            ConsoleUi.KeyValue("Agent version", agent != null ? agent.Version : "unavailable" + (agentError != null ? " (" + agentError + ")" : string.Empty));
            ConsoleUi.KeyValue("Update source", source);
            if (!Text.IsBlank(policy.TargetGroup)) ConsoleUi.KeyValue("WSUS target group", policy.TargetGroup);
            ConsoleUi.KeyValue("Automatic updates", policy.DescribeMode());
            ConsoleUi.KeyValue("Microsoft Update", muRegistered ? "Registered (other Microsoft products included)" : "Not registered (Windows only)");
            if (agent != null)
            {
                ConsoleUi.KeyValue("Last successful scan", Text.FormatDate(agent.LastSearchSuccess));
                ConsoleUi.KeyValue("Last install", Text.FormatDate(agent.LastInstallSuccess));
            }
            if (busy.HasValue) ConsoleUi.KeyValue("Installer busy", Text.YesNo(busy.Value));
            if (services.Count > 0)
            {
                ConsoleUi.KeyValue("Services", Text.Join(", ", services.Select(x => x.Name + "=" + x.Status + " (" + x.StartMode + ")")));
                var wu = services.FirstOrDefault(x => Text.EqualsIgnoreCase(x.Name, "wuauserv"));
                if (wu != null && Text.EqualsIgnoreCase(wu.StartMode, "Disabled"))
                    ConsoleUi.Warn("The Windows Update service is disabled. Fix: " + ExeName + " services startup wuauserv manual");
            }

            PrintReboot(reboot);
        }

        public static string DescribeEffectiveSource(AutoUpdatePolicy policy, IList<UpdateServiceInfo> services)
        {
            if (policy.UsesWsus) return "WSUS (" + policy.WUServer + ")";
            var def = services.FirstOrDefault(x => x.IsDefaultAUService);
            if (def != null) return def.Name + " (internet)";
            return policy.DescribeSource();
        }

        // ------------------------------------------------------------------ reboot

        public List<PendingRebootReason> CollectRebootReasons()
        {
            var reasons = s.Environment.GetPendingRebootReasons();
            try
            {
                var agent = s.Updates.GetAgentInfo();
                if (agent != null && agent.RebootRequired && !reasons.Any(r => r.Source.StartsWith("Windows Update", StringComparison.Ordinal)))
                    reasons.Add(new PendingRebootReason("Windows Update Agent reports a reboot is required", true));
            }
            catch (Exception ex)
            {
                ConsoleUi.Log.Warn("Could not query Windows Update Agent reboot state: " + ex.Message);
            }
            return reasons;
        }

        public bool ShowPendingReboot()
        {
            var reasons = CollectRebootReasons();
            if (json)
            {
                var w = new JsonWriter().BeginObject();
                WriteRebootJson(w, reasons);
                ConsoleUi.Raw(w.EndObject().ToString());
            }
            else
            {
                PrintReboot(reasons);
            }
            return reasons.Any(r => r.Definite);
        }

        private static void PrintReboot(List<PendingRebootReason> reasons)
        {
            ConsoleUi.Header("Restart");
            bool pending = reasons.Any(r => r.Definite);
            ConsoleUi.KeyValue("Restart pending", pending ? "YES" : "No", pending ? ConsoleColor.Yellow : ConsoleColor.Green);
            foreach (var r in reasons)
            {
                ConsoleUi.Line("      - " + r.Source + (r.Definite ? string.Empty : " (informational)"));
            }
        }

        private static void WriteRebootJson(JsonWriter w, List<PendingRebootReason> reasons)
        {
            w.BeginObject("reboot").Property("pending", reasons.Any(r => r.Definite)).BeginArray("reasons");
            foreach (var r in reasons)
            {
                w.BeginObject().Property("source", r.Source).Property("definite", r.Definite).EndObject();
            }
            w.EndArray().EndObject();
        }

        private int CmdReboot(ParsedArgs a)
        {
            RequireAdmin();
            if (a.Flag("abort"))
            {
                s.Power.AbortRestart();
                ConsoleUi.Success("The scheduled restart was cancelled.");
                return ExitCodes.Success;
            }
            int delay = a.GetInt("delay", 60, 0, 86400);
            return ScheduleRestart(delay, a.Flag("yes")) ? ExitCodes.Success : ExitCodes.Error;
        }

        public bool ScheduleRestart(int delaySeconds, bool assumeYes)
        {
            RequireAdmin();
            var answer = ConsoleUi.Confirm("Restart " + System.Environment.MachineName + " in " + delaySeconds + " seconds?", assumeYes);
            if (answer != true)
            {
                if (answer == null) ConsoleUi.Error("No interactive input available. Use --yes to confirm.");
                else ConsoleUi.Info("Restart cancelled.");
                return false;
            }
            s.Power.ScheduleRestart(delaySeconds, "WinUpdateManager: restarting to complete Windows Update maintenance.");
            ConsoleUi.Warn("Restart scheduled in " + delaySeconds + " seconds. Cancel with: " + ExeName + " reboot --abort");
            return true;
        }

        // ------------------------------------------------------------------ search / list

        public static SearchOptions BuildSearchOptions(ParsedArgs a, SearchScope scope)
        {
            return new SearchOptions
            {
                Source = SearchCriteria.ParseSource(a.Get("source")),
                Scope = scope,
                IncludeDrivers = a.Flag("include-drivers"),
                RawCriteria = a.Get("criteria")
            };
        }

        public IList<UpdateInfo> Scan(SearchOptions options, UpdateFilter filter)
        {
            string scopeName = options.Scope == SearchScope.Installed ? "installed" : options.Scope == SearchScope.Hidden ? "hidden" : "available";
            ConsoleUi.Info("Searching for " + scopeName + " updates from " + SearchCriteria.DescribeSource(options.Source) + " (this can take a few minutes)...");
            ConsoleUi.Log.Info("Search criteria: " + SearchCriteria.Build(options) + " | filter: " + filter.Describe());

            var sw = Stopwatch.StartNew();
            var all = s.Updates.Search(options);
            var matched = filter.Apply(all);
            sw.Stop();

            int skippedOptional = all.Count(u => u.IsOptional && !filter.Matches(u));
            ConsoleUi.Info(string.Format(CultureInfo.InvariantCulture,
                "Search finished in {0}: {1} update(s) found, {2} selected.",
                Text.FormatDuration(sw.Elapsed), all.Count, matched.Count));
            if (skippedOptional > 0 && !filter.IncludeOptional)
                ConsoleUi.Detail(skippedOptional + " optional update(s) not shown (use --include-optional).");
            return matched;
        }

        private int CmdList(ParsedArgs a)
        {
            if (a.Flag("hidden") && a.Flag("installed")) throw new UsageException("Use either --hidden or --installed, not both.");
            var scope = a.Flag("hidden") ? SearchScope.Hidden : a.Flag("installed") ? SearchScope.Installed : SearchScope.Available;
            var filter = UpdateFilter.FromArgs(a);
            if (scope != SearchScope.Available)
            {
                filter.IncludeOptional = true;
            }
            var updates = Scan(BuildSearchOptions(a, scope), filter);

            if (json)
            {
                var w = new JsonWriter().BeginObject().Property("count", updates.Count).Property("totalDownloadBytes", updates.Sum(u => u.MaxDownloadSize));
                WriteUpdatesJson(w, "updates", updates);
                ConsoleUi.Raw(w.EndObject().ToString());
                return ExitCodes.Success;
            }

            if (updates.Count == 0)
            {
                ConsoleUi.Success(scope == SearchScope.Available ? "No updates available for the selected criteria - the server is up to date." : "No updates found.");
                return ExitCodes.Success;
            }
            PrintUpdateTable(updates);
            PrintSelectionSummary(updates);
            return ExitCodes.Success;
        }

        public void PrintUpdateTable(IList<UpdateInfo> updates)
        {
            var rows = new List<string[]>();
            int i = 0;
            foreach (var u in updates)
            {
                i++;
                var flags = new List<string>();
                if (u.IsDownloaded && !u.IsInstalled) flags.Add("downloaded");
                if (u.IsOptional) flags.Add("optional");
                if (u.IsHidden) flags.Add("hidden");
                if (u.Kind == UpdateKind.Driver) flags.Add("driver");
                rows.Add(new[]
                {
                    i.ToString(CultureInfo.InvariantCulture),
                    u.KbDisplay,
                    u.ClassificationDisplay,
                    u.SeverityDisplay,
                    Text.FormatSize(u.MaxDownloadSize),
                    u.IsInstalled ? "-" : (u.MayRequireReboot ? "maybe" : "no"),
                    (flags.Count > 0 ? "[" + Text.Join(", ", flags) + "] " : string.Empty) + u.Title
                });
            }
            ConsoleUi.Line();
            ConsoleUi.Table(new[] { "#", "KB", "Classification", "Severity", "Size", "Reboot", "Title" }, rows, 6);
        }

        private static void PrintSelectionSummary(IList<UpdateInfo> updates)
        {
            ConsoleUi.Line();
            ConsoleUi.Info(string.Format(CultureInfo.InvariantCulture, "{0} update(s), total download up to {1}, {2} may require a restart.",
                updates.Count, Text.FormatSize(updates.Sum(u => u.MaxDownloadSize)), updates.Count(u => u.MayRequireReboot)));
        }

        private static void WriteUpdatesJson(JsonWriter w, string name, IEnumerable<UpdateInfo> updates)
        {
            w.BeginArray(name);
            foreach (var u in updates) WriteUpdateJson(w, u);
            w.EndArray();
        }

        private static void WriteUpdateJson(JsonWriter w, UpdateInfo u)
        {
            w.BeginObject()
                .Property("id", u.Id)
                .Property("revision", u.Revision)
                .Property("title", u.Title)
                .Property("kb", u.KbArticleIds.Select(k => "KB" + k))
                .Property("classifications", u.Classifications)
                .Property("products", u.Products)
                .Property("severity", u.Severity)
                .Property("type", u.Kind.ToString())
                .Property("maxDownloadBytes", u.MaxDownloadSize)
                .Property("isDownloaded", u.IsDownloaded)
                .Property("isInstalled", u.IsInstalled)
                .Property("isHidden", u.IsHidden)
                .Property("isOptional", u.IsOptional)
                .Property("isMandatory", u.IsMandatory)
                .Property("isUninstallable", u.IsUninstallable)
                .Property("mayRequireReboot", u.MayRequireReboot)
                .Property("releaseDate", u.ReleaseDate)
                .Property("supportUrl", u.SupportUrl)
                .EndObject();
        }

        // ------------------------------------------------------------------ download / install

        private int CmdProcess(ParsedArgs a, bool downloadOnly)
        {
            var settings = new ProcessSettings
            {
                DownloadOnly = downloadOnly,
                AcceptEula = !a.Flag("no-accept-eula"),
                DryRun = a.Flag("dry-run"),
                AssumeYes = a.Flag("yes"),
                Force = a.Flag("force"),
                Reboot = PolicyParsing.ParseRebootMode(a.Get("reboot"), RebootMode.Never),
                RebootDelaySeconds = a.GetInt("reboot-delay", 120, 0, 86400)
            };
            if (!settings.DryRun) RequireAdmin();

            var updates = Scan(BuildSearchOptions(a, SearchScope.Available), UpdateFilter.FromArgs(a));
            if (updates.Count > 0 && !json)
            {
                PrintUpdateTable(updates);
                PrintSelectionSummary(updates);
            }
            var outcome = ProcessUpdates(updates, settings);
            if (json) WriteOutcomeJson(outcome, downloadOnly ? "download" : "install");
            return outcome.ExitCode;
        }

        public ProcessOutcome ProcessUpdates(IList<UpdateInfo> updates, ProcessSettings settings)
        {
            var outcome = new ProcessOutcome();
            string verb = settings.DownloadOnly ? "download" : "install";

            if (updates.Count == 0)
            {
                ConsoleUi.Success("Nothing to " + verb + " - no matching updates.");
                if (!settings.DownloadOnly)
                {
                    outcome.RebootRequired = CollectRebootReasons().Any(r => r.Definite);
                    if (outcome.RebootRequired) outcome.RestartScheduled = HandleRestart(true, settings);
                }
                return outcome;
            }

            if (settings.DryRun)
            {
                ConsoleUi.Success("Dry run: " + updates.Count + " update(s) would be " + (settings.DownloadOnly ? "downloaded" : "installed") + ". Nothing was changed.");
                return outcome;
            }

            RequireAdmin();

            var answer = ConsoleUi.Confirm("Do you want to " + verb + " " + updates.Count + " update(s)?", settings.AssumeYes);
            if (answer != true)
            {
                if (answer == null)
                {
                    ConsoleUi.Error("No interactive input available. Re-run with --yes to " + verb + " without prompting.");
                    outcome.NoInput = true;
                }
                else
                {
                    ConsoleUi.Info("Cancelled - nothing was changed.");
                }
                outcome.Cancelled = true;
                return outcome;
            }

            if (!settings.DownloadOnly)
            {
                if (SafeCheck(() => s.Updates.IsInstallerBusy()))
                {
                    ConsoleUi.Error("Another Windows Update installation is in progress. Try again when it has finished.");
                    outcome.Aborted = true;
                    return outcome;
                }
                if (SafeCheck(() => s.Updates.IsRebootRequiredBeforeInstallation()) && !settings.Force)
                {
                    ConsoleUi.Error("Windows requires a restart before more updates can be installed. Restart first (or use --force).");
                    outcome.RebootRequired = true;
                    outcome.RestartScheduled = HandleRestart(true, settings);
                    return outcome;
                }
            }

            cancelRequested = false;
            int index = 0;
            foreach (var update in updates)
            {
                index++;
                if (cancelRequested)
                {
                    ConsoleUi.Warn("Stopped by user. Remaining updates were skipped.");
                    outcome.Cancelled = true;
                    break;
                }

                ConsoleUi.Line();
                ConsoleUi.Colored(string.Format(CultureInfo.InvariantCulture, "[{0}/{1}] {2} {3}", index, updates.Count, update.KbDisplay, update.Title), ConsoleColor.White);
                ConsoleUi.Log.Info(string.Format(CultureInfo.InvariantCulture, "[{0}/{1}] {2} {3} ({4})", index, updates.Count, update.KbDisplay, update.Title, update.Id));

                var result = ProcessSingle(update, settings);
                outcome.Results.Add(result);
                if (result.RebootRequired) outcome.RebootRequired = true;
            }

            PrintOutcome(outcome, settings);
            if (!settings.DownloadOnly)
            {
                if (!outcome.RebootRequired) outcome.RebootRequired = CollectRebootReasons().Any(r => r.Definite);
                outcome.RestartScheduled = HandleRestart(outcome.RebootRequired, settings);
            }
            return outcome;
        }

        private UpdateActionResult ProcessSingle(UpdateInfo update, ProcessSettings settings)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                if (!update.EulaAccepted)
                {
                    if (!settings.AcceptEula)
                    {
                        ConsoleUi.Warn("Skipped: this update requires accepting a license agreement (--no-accept-eula).");
                        return new UpdateActionResult { Update = update, Action = "skip", Result = OperationResult.Skipped, Message = "EULA not accepted" };
                    }
                    s.Updates.AcceptEula(update);
                    ConsoleUi.Detail("License agreement accepted.");
                }

                if (!update.IsDownloaded)
                {
                    ConsoleUi.Detail("Downloading (" + Text.FormatSize(update.MaxDownloadSize) + ")...");
                    var download = s.Updates.Download(update);
                    if (!download.IsSuccess)
                    {
                        string msg = "Download failed: " + (download.HResult != 0 ? WuaErrors.Format(download.HResult) : download.Result.ToString());
                        ConsoleUi.Error(msg);
                        download.Message = msg;
                        download.Duration = sw.Elapsed;
                        return download;
                    }
                    ConsoleUi.Detail("Downloaded in " + Text.FormatDuration(sw.Elapsed) + ".");
                }
                else
                {
                    ConsoleUi.Detail("Already downloaded.");
                }

                if (settings.DownloadOnly)
                {
                    ConsoleUi.Success("      Ready to install.");
                    return new UpdateActionResult { Update = update, Action = "download", Result = OperationResult.Succeeded, Duration = sw.Elapsed };
                }

                ConsoleUi.Detail("Installing...");
                var install = s.Updates.Install(update);
                install.Duration = sw.Elapsed;
                if (install.IsSuccess)
                {
                    ConsoleUi.Success("      Installed" + (install.Result == OperationResult.SucceededWithErrors ? " with errors" : string.Empty) +
                                      (install.RebootRequired ? " - restart required" : string.Empty) +
                                      " (" + Text.FormatDuration(sw.Elapsed) + ").");
                }
                else
                {
                    install.Message = "Installation failed: " + (install.HResult != 0 ? WuaErrors.Format(install.HResult) : install.Result.ToString());
                    ConsoleUi.Error(install.Message);
                }
                return install;
            }
            catch (ApiException ex)
            {
                string msg = ex.Message + (ex.ErrorCode != 0 ? " [" + WuaErrors.Format(ex.ErrorCode) + "]" : string.Empty);
                ConsoleUi.Error(msg);
                return new UpdateActionResult { Update = update, Action = settings.DownloadOnly ? "download" : "install", Result = OperationResult.Failed, HResult = ex.ErrorCode, Message = msg, Duration = sw.Elapsed };
            }
        }

        private void PrintOutcome(ProcessOutcome outcome, ProcessSettings settings)
        {
            if (outcome.Results.Count == 0) return;
            ConsoleUi.Header(settings.DownloadOnly ? "Download summary" : "Installation summary");
            var rows = outcome.Results.Select(r => new[]
            {
                r.Update.KbDisplay,
                ResultName(r.Result),
                r.RebootRequired ? "yes" : "no",
                Text.FormatDuration(r.Duration),
                r.Message ?? r.Update.Title
            }).ToList();
            ConsoleUi.Table(new[] { "KB", "Result", "Restart", "Time", "Details" }, rows, 4);
            ConsoleUi.Line();
            string text = string.Format(CultureInfo.InvariantCulture, "{0} succeeded, {1} failed, {2} skipped.",
                outcome.Succeeded, outcome.Failed, outcome.Results.Count(r => r.Result == OperationResult.Skipped));
            if (outcome.Failed > 0) ConsoleUi.Warn(text);
            else ConsoleUi.Success(text);
        }

        private bool HandleRestart(bool required, ProcessSettings settings)
        {
            if (settings.Reboot == RebootMode.Always || (settings.Reboot == RebootMode.IfRequired && required))
            {
                s.Power.ScheduleRestart(settings.RebootDelaySeconds, "WinUpdateManager: restarting to finish installing Windows updates.");
                ConsoleUi.Warn("The server will restart in " + settings.RebootDelaySeconds + " seconds. Cancel with: " + ExeName + " reboot --abort");
                return true;
            }
            if (required)
                ConsoleUi.Warn("A restart is required to finish installing updates. Run '" + ExeName + " reboot' when convenient (or use --reboot if-required).");
            return false;
        }

        private void WriteOutcomeJson(ProcessOutcome outcome, string action)
        {
            var w = new JsonWriter().BeginObject()
                .Property("action", action)
                .Property("succeeded", outcome.Succeeded)
                .Property("failed", outcome.Failed)
                .Property("cancelled", outcome.Cancelled)
                .Property("rebootRequired", outcome.RebootRequired)
                .Property("restartScheduled", outcome.RestartScheduled)
                .Property("exitCode", outcome.ExitCode)
                .BeginArray("results");
            foreach (var r in outcome.Results)
            {
                w.BeginObject()
                    .Property("kb", r.Update.KbDisplay)
                    .Property("id", r.Update.Id)
                    .Property("title", r.Update.Title)
                    .Property("action", r.Action)
                    .Property("result", ResultName(r.Result))
                    .Property("hresult", r.HResult != 0 ? Text.Hex(r.HResult) : null)
                    .Property("rebootRequired", r.RebootRequired)
                    .Property("seconds", (long)r.Duration.TotalSeconds)
                    .Property("message", r.Message)
                    .EndObject();
            }
            ConsoleUi.Raw(w.EndArray().EndObject().ToString());
        }

        public static string ResultName(OperationResult result)
        {
            switch (result)
            {
                case OperationResult.Succeeded: return "Succeeded";
                case OperationResult.SucceededWithErrors: return "Succeeded with errors";
                case OperationResult.Failed: return "Failed";
                case OperationResult.Aborted: return "Aborted";
                case OperationResult.InProgress: return "In progress";
                case OperationResult.Skipped: return "Skipped";
                default: return "Not started";
            }
        }

        private static bool SafeCheck(Func<bool> check)
        {
            try
            {
                return check();
            }
            catch (Exception ex)
            {
                ConsoleUi.Log.Warn("Pre-install check failed: " + ex.Message);
                return false;
            }
        }

        // ------------------------------------------------------------------ uninstall

        private int CmdUninstall(ParsedArgs a)
        {
            if (!a.Has("kb") && !a.Has("id")) throw new UsageException("Specify the update to remove with --kb <KB> (or --id <guid>).");
            var settings = new ProcessSettings
            {
                DryRun = a.Flag("dry-run"),
                AssumeYes = a.Flag("yes"),
                Reboot = PolicyParsing.ParseRebootMode(a.Get("reboot"), RebootMode.Never),
                RebootDelaySeconds = a.GetInt("reboot-delay", 120, 0, 86400)
            };
            if (!settings.DryRun) RequireAdmin();

            var filter = UpdateFilter.FromArgs(a);
            var options = BuildSearchOptions(a, SearchScope.Installed);
            options.IncludeDrivers = true;
            var updates = Scan(options, filter);
            var outcome = UninstallUpdates(updates, settings);
            if (json) WriteOutcomeJson(outcome, "uninstall");
            return outcome.ExitCode;
        }

        public ProcessOutcome UninstallUpdates(IList<UpdateInfo> updates, ProcessSettings settings)
        {
            var outcome = new ProcessOutcome();
            if (updates.Count == 0)
            {
                ConsoleUi.Warn("No installed update matches. Check the KB number with: " + ExeName + " list --installed --kb <KB>");
                return outcome;
            }
            PrintUpdateTable(updates);
            if (settings.DryRun)
            {
                ConsoleUi.Success("Dry run: " + updates.Count + " update(s) would be uninstalled. Nothing was changed.");
                return outcome;
            }
            RequireAdmin();
            var answer = ConsoleUi.Confirm("Uninstall " + updates.Count + " update(s)?", settings.AssumeYes);
            if (answer != true)
            {
                if (answer == null)
                {
                    ConsoleUi.Error("No interactive input available. Use --yes to confirm.");
                    outcome.NoInput = true;
                }
                else ConsoleUi.Info("Cancelled - nothing was changed.");
                outcome.Cancelled = true;
                return outcome;
            }

            foreach (var u in updates)
            {
                ConsoleUi.Line();
                ConsoleUi.Colored("Uninstalling " + u.KbDisplay + " " + u.Title, ConsoleColor.White);
                UpdateActionResult result;
                if (!u.IsUninstallable)
                {
                    string kb = u.KbArticleIds.Count > 0 ? u.KbArticleIds[0] : "<number>";
                    result = new UpdateActionResult
                    {
                        Update = u,
                        Action = "uninstall",
                        Result = OperationResult.Failed,
                        Message = "Windows Update Agent cannot uninstall this update. Try: wusa.exe /uninstall /kb:" + kb +
                                  " /quiet /norestart  or  DISM /Online /Get-Packages + DISM /Online /Remove-Package"
                    };
                    ConsoleUi.Error(result.Message);
                }
                else
                {
                    try
                    {
                        result = s.Updates.Uninstall(u);
                        if (result.IsSuccess) ConsoleUi.Success("      Uninstalled" + (result.RebootRequired ? " - restart required." : "."));
                        else
                        {
                            result.Message = "Uninstall failed: " + (result.HResult != 0 ? WuaErrors.Format(result.HResult) : ResultName(result.Result));
                            ConsoleUi.Error(result.Message);
                        }
                    }
                    catch (ApiException ex)
                    {
                        result = new UpdateActionResult { Update = u, Action = "uninstall", Result = OperationResult.Failed, HResult = ex.ErrorCode, Message = ex.Message };
                        ConsoleUi.Error(ex.Message + " [" + WuaErrors.Format(ex.ErrorCode) + "]");
                    }
                }
                outcome.Results.Add(result);
                if (result.RebootRequired) outcome.RebootRequired = true;
            }
            PrintOutcome(outcome, settings);
            outcome.RestartScheduled = HandleRestart(outcome.RebootRequired, settings);
            return outcome;
        }

        // ------------------------------------------------------------------ hide / unhide

        private int CmdHide(ParsedArgs a, bool hide)
        {
            var filter = UpdateFilter.FromArgs(a);
            if (!filter.HasCriteria && !a.Flag("all"))
                throw new UsageException("Select updates with a filter (for example --kb KB5030216 or --title Preview), or use --all.");
            if (!a.Flag("dry-run")) RequireAdmin();

            filter.IncludeOptional = true;
            filter.IncludeDrivers = true;
            var options = BuildSearchOptions(a, hide ? SearchScope.Available : SearchScope.Hidden);
            options.IncludeDrivers = true;
            var updates = Scan(options, filter);
            if (updates.Count == 0)
            {
                ConsoleUi.Warn("No matching " + (hide ? "visible" : "hidden") + " updates.");
                return ExitCodes.Success;
            }
            PrintUpdateTable(updates);
            if (a.Flag("dry-run"))
            {
                ConsoleUi.Success("Dry run: " + updates.Count + " update(s) would be " + (hide ? "hidden" : "unhidden") + ".");
                return ExitCodes.Success;
            }
            return HideUpdates(updates, hide, a.Flag("yes")) ? ExitCodes.Success : ExitCodes.Error;
        }

        public bool HideUpdates(IList<UpdateInfo> updates, bool hide, bool assumeYes)
        {
            RequireAdmin();
            string verb = hide ? "Hide" : "Unhide";
            var answer = ConsoleUi.Confirm(verb + " " + updates.Count + " update(s)?", assumeYes);
            if (answer != true)
            {
                if (answer == null) ConsoleUi.Error("No interactive input available. Use --yes to confirm.");
                else ConsoleUi.Info("Cancelled.");
                return answer == false;
            }
            int failed = 0;
            foreach (var u in updates)
            {
                try
                {
                    s.Updates.SetHidden(u, hide);
                    ConsoleUi.Success("  " + (hide ? "Hidden:   " : "Unhidden: ") + u.KbDisplay + " " + u.Title);
                }
                catch (ApiException ex)
                {
                    failed++;
                    ConsoleUi.Error(u.KbDisplay + ": " + ex.Message + " [" + WuaErrors.Format(ex.ErrorCode) + "]");
                }
            }
            return failed == 0;
        }

        // ------------------------------------------------------------------ history

        private int CmdHistory(ParsedArgs a)
        {
            int count = a.GetInt("count", 30, 1, 5000);
            ShowHistory(count, Text.SplitList(a.Get("kb")).Select(Text.NormalizeKb).ToList(), a.Get("title"));
            return ExitCodes.Success;
        }

        public void ShowHistory(int count, List<string> kbs, string title)
        {
            var entries = s.Updates.GetHistory(kbs.Count > 0 || title != null ? Math.Max(count, 1000) : count)
                .Where(e => kbs.Count == 0 || (e.Kb != null && kbs.Contains(e.Kb)))
                .Where(e => title == null || Text.ContainsIgnoreCase(e.Title, title))
                .Take(count)
                .ToList();

            if (json)
            {
                var w = new JsonWriter().BeginObject().Property("count", entries.Count).BeginArray("history");
                foreach (var e in entries)
                {
                    w.BeginObject()
                        .Property("date", e.Date)
                        .Property("operation", e.Operation)
                        .Property("result", ResultName(e.Result))
                        .Property("hresult", e.HResult != 0 ? Text.Hex(e.HResult) : null)
                        .Property("kb", e.Kb != null ? "KB" + e.Kb : null)
                        .Property("title", e.Title)
                        .Property("updateId", e.UpdateId)
                        .Property("client", e.ClientApplication)
                        .EndObject();
                }
                ConsoleUi.Raw(w.EndArray().EndObject().ToString());
                return;
            }

            if (entries.Count == 0)
            {
                ConsoleUi.Info("No Windows Update history entries found.");
                return;
            }
            var rows = entries.Select(e => new[]
            {
                Text.FormatDate(e.Date),
                e.Operation,
                ResultName(e.Result) + (e.HResult != 0 && e.Result != OperationResult.Succeeded ? " " + Text.Hex(e.HResult) : string.Empty),
                e.Kb != null ? "KB" + e.Kb : "-",
                e.Title ?? "(no title)"
            }).ToList();
            ConsoleUi.Line();
            ConsoleUi.Table(new[] { "Date", "Operation", "Result", "KB", "Title" }, rows, 4);
            ConsoleUi.Line();
            ConsoleUi.Info(entries.Count + " entr" + (entries.Count == 1 ? "y" : "ies") + " shown, " +
                           entries.Count(e => e.Result == OperationResult.Failed) + " failed.");
        }

        // ------------------------------------------------------------------ config (policy)

        private int CmdConfig(ParsedArgs a)
        {
            string sub = (a.Positional(0) ?? "show").ToLowerInvariant();
            switch (sub)
            {
                case "show":
                    ShowPolicy();
                    return ExitCodes.Success;

                case "set":
                    {
                        var change = new PolicyChange();
                        if (a.Has("mode")) change.Mode = PolicyParsing.ParseMode(a.Get("mode"));
                        if (a.Has("day")) change.Day = PolicyParsing.ParseDay(a.Get("day"));
                        if (a.Has("time"))
                        {
                            int hour, minute;
                            PolicyParsing.ParseTime(a.Get("time"), out hour, out minute);
                            if (minute != 0) ConsoleUi.Warn("Automatic Updates schedules use whole hours; minutes are ignored.");
                            change.Hour = hour;
                        }
                        if (a.Has("wsus-server"))
                        {
                            PolicyParsing.ValidateServerUrl(a.Get("wsus-server"), "--wsus-server");
                            change.WsusServer = a.Get("wsus-server").Trim().TrimEnd('/');
                        }
                        if (a.Has("wsus-status-server"))
                        {
                            PolicyParsing.ValidateServerUrl(a.Get("wsus-status-server"), "--wsus-status-server");
                            change.WsusStatusServer = a.Get("wsus-status-server").Trim().TrimEnd('/');
                        }
                        if (a.Has("target-group")) change.TargetGroup = a.Get("target-group").Trim();
                        change.ClearWsus = a.Flag("clear-wsus");
                        if (change.ClearWsus && change.WsusServer != null) throw new UsageException("Use either --wsus-server or --clear-wsus, not both.");
                        if (change.IsEmpty)
                            throw new UsageException("Nothing to change. Use --mode, --day, --time, --wsus-server, --target-group or --clear-wsus.");
                        ApplyPolicy(change, a.Flag("restart-service"));
                        return ExitCodes.Success;
                    }

                case "reset":
                    {
                        RequireAdmin();
                        var answer = ConsoleUi.Confirm("Remove all Windows Update policy values (automatic update mode, schedule and WSUS)?", a.Flag("yes"));
                        if (answer != true)
                        {
                            if (answer == null) ConsoleUi.Error("No interactive input available. Use --yes to confirm.");
                            return answer == null ? ExitCodes.Error : ExitCodes.Success;
                        }
                        ApplyPolicy(new PolicyChange { ResetAll = true }, a.Flag("restart-service"));
                        return ExitCodes.Success;
                    }

                default:
                    throw new UsageException("Unknown config action '" + sub + "'. Use: show, set, reset.");
            }
        }

        public void ShowPolicy()
        {
            var p = s.Policy.Read();
            if (json)
            {
                var w = new JsonWriter().BeginObject()
                    .Property("configured", p.IsConfigured)
                    .Property("mode", p.DescribeMode())
                    .Property("source", p.DescribeSource())
                    .Property("NoAutoUpdate", p.NoAutoUpdate)
                    .Property("AUOptions", p.AUOptions)
                    .Property("ScheduledInstallDay", p.ScheduledInstallDay)
                    .Property("ScheduledInstallTime", p.ScheduledInstallTime)
                    .Property("UseWUServer", p.UseWUServer)
                    .Property("WUServer", p.WUServer)
                    .Property("WUStatusServer", p.WUStatusServer)
                    .Property("TargetGroup", p.TargetGroup)
                    .Property("TargetGroupEnabled", p.TargetGroupEnabled)
                    .Property("DoNotConnectToWindowsUpdateInternetLocations", p.DoNotConnectToWindowsUpdateInternetLocations)
                    .EndObject();
                ConsoleUi.Raw(w.ToString());
                return;
            }
            ConsoleUi.Header("Windows Update policy");
            ConsoleUi.KeyValue("Automatic updates", p.DescribeMode());
            ConsoleUi.KeyValue("Update source", p.DescribeSource());
            if (p.UsesWsus) ConsoleUi.KeyValue("WSUS status server", p.WUStatusServer ?? "-");
            if (p.TargetGroupEnabled == 1) ConsoleUi.KeyValue("WSUS target group", p.TargetGroup ?? "-");
            if (p.DoNotConnectToWindowsUpdateInternetLocations == 1) ConsoleUi.KeyValue("Internet WU locations", "Blocked by policy");
            ConsoleUi.Line();
            ConsoleUi.Line("  Registry: HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate[\\AU]");
            ConsoleUi.Line("  Note: domain Group Policy overrides these values on the next policy refresh.");
        }

        public void ApplyPolicy(PolicyChange change, bool restartService)
        {
            RequireAdmin();
            var current = s.Policy.Read();
            var updated = change.ApplyTo(current);
            s.Policy.Write(updated);
            ConsoleUi.Success("Windows Update policy saved.");
            ConsoleUi.KeyValue("Automatic updates", updated.DescribeMode());
            ConsoleUi.KeyValue("Update source", updated.DescribeSource());

            if (restartService)
            {
                RestartService("wuauserv");
            }
            else
            {
                ConsoleUi.Info("Restart the Windows Update service to apply now: " + ExeName + " services restart wuauserv");
            }
        }

        // ------------------------------------------------------------------ services

        private int CmdServices(ParsedArgs a)
        {
            string sub = (a.Positional(0) ?? "status").ToLowerInvariant();
            string name = a.Positional(1) ?? "wuauserv";
            switch (sub)
            {
                case "status":
                case "list":
                    ShowServices();
                    return ExitCodes.Success;
                case "start":
                    RequireAdmin();
                    ConsoleUi.Info("Starting " + name + "...");
                    s.Services.Start(name);
                    ConsoleUi.Success(name + " is running.");
                    return ExitCodes.Success;
                case "stop":
                    RequireAdmin();
                    ConsoleUi.Info("Stopping " + name + "...");
                    s.Services.Stop(name);
                    ConsoleUi.Success(name + " is stopped.");
                    return ExitCodes.Success;
                case "restart":
                    RestartService(name);
                    return ExitCodes.Success;
                case "startup":
                    {
                        if (a.Positionals.Count < 3) throw new UsageException("Usage: " + ExeName + " services startup <service> automatic|manual|disabled");
                        var mode = PolicyParsing.ParseStartupMode(a.Positional(2));
                        RequireAdmin();
                        s.Services.SetStartupMode(name, mode);
                        ConsoleUi.Success(name + " startup type set to " + mode + ".");
                        return ExitCodes.Success;
                    }
                case "reset":
                case "repair":
                    return ResetComponents(a.Flag("yes")) ? ExitCodes.Success : ExitCodes.Error;
                default:
                    throw new UsageException("Unknown services action '" + sub + "'. Use: status, start, stop, restart, startup, reset.");
            }
        }

        public void ShowServices()
        {
            var list = s.Services.GetUpdateServices();
            if (json)
            {
                var w = new JsonWriter().BeginObject().BeginArray("services");
                foreach (var x in list)
                {
                    w.BeginObject().Property("name", x.Name).Property("displayName", x.DisplayName).Property("status", x.Status).Property("startMode", x.StartMode).EndObject();
                }
                ConsoleUi.Raw(w.EndArray().EndObject().ToString());
                return;
            }
            ConsoleUi.Line();
            ConsoleUi.Table(new[] { "Service", "Status", "Startup", "Display name" },
                list.Select(x => new[] { x.Name, x.Status, x.StartMode, x.DisplayName }).ToList(), 3);
        }

        public void RestartService(string name)
        {
            RequireAdmin();
            ConsoleUi.Info("Restarting " + name + "...");
            s.Services.Stop(name);
            s.Services.Start(name);
            ConsoleUi.Success(name + " restarted.");
        }

        public bool ResetComponents(bool assumeYes)
        {
            RequireAdmin();
            ConsoleUi.Warn("This stops Windows Update services, renames SoftwareDistribution and catroot2, clears the BITS queue and restarts the services. " +
                           "Update history in the Settings app will look empty afterwards (the real history is kept by Windows).");
            var answer = ConsoleUi.Confirm("Reset Windows Update components now?", assumeYes);
            if (answer != true)
            {
                if (answer == null) ConsoleUi.Error("No interactive input available. Use --yes to confirm.");
                else ConsoleUi.Info("Cancelled.");
                return answer == false;
            }
            s.Services.ResetUpdateComponents(msg => ConsoleUi.Detail(msg));
            ConsoleUi.Success("Windows Update components were reset. Run '" + ExeName + " list' to scan again.");
            return true;
        }

        // ------------------------------------------------------------------ schedule

        private int CmdSchedule(ParsedArgs a)
        {
            string sub = (a.Positional(0) ?? "show").ToLowerInvariant();
            string name = a.Get("name", ScheduleDefinition.DefaultTaskName);
            switch (sub)
            {
                case "show":
                case "status":
                    ShowSchedule(name);
                    return ExitCodes.Success;

                case "create":
                case "set":
                    {
                        var def = new ScheduleDefinition { TaskName = name };
                        string freq = (a.Get("frequency") ?? "weekly").ToLowerInvariant();
                        if (freq == "daily") def.Frequency = ScheduleFrequency.Daily;
                        else if (freq == "weekly") def.Frequency = ScheduleFrequency.Weekly;
                        else throw new UsageException("--frequency must be daily or weekly.");

                        if (def.Frequency == ScheduleFrequency.Weekly)
                            def.Day = PolicyParsing.ToDayOfWeek(PolicyParsing.ParseDay(a.Get("day") ?? "sunday"));
                        else if (a.Has("day"))
                            throw new UsageException("--day is only used with --frequency weekly.");

                        int hour, minute;
                        PolicyParsing.ParseTime(a.Get("at") ?? "03:00", out hour, out minute);
                        def.Hour = hour;
                        def.Minute = minute;

                        var reboot = PolicyParsing.ParseRebootMode(a.Get("reboot"), RebootMode.IfRequired);
                        int delay = a.GetInt("reboot-delay", 300, 0, 86400);
                        UpdateFilter.FromArgs(a); // validate filters early
                        def.Arguments = BuildScheduledArguments(a, reboot, delay);
                        CreateSchedule(def);
                        return ExitCodes.Success;
                    }

                case "delete":
                case "remove":
                    RequireAdmin();
                    if (s.Scheduler.Delete(name)) ConsoleUi.Success("Scheduled task '" + name + "' deleted.");
                    else ConsoleUi.Warn("Scheduled task '" + name + "' does not exist.");
                    return ExitCodes.Success;

                default:
                    throw new UsageException("Unknown schedule action '" + sub + "'. Use: show, create, delete.");
            }
        }

        /// <summary>Builds the command line the scheduled task runs: install --yes + reboot + filters.</summary>
        public static string BuildScheduledArguments(ParsedArgs a, RebootMode reboot, int rebootDelay)
        {
            var parts = new List<string> { "install", "--yes", "--no-color", "--reboot", PolicyParsing.RebootModeName(reboot) };
            if (reboot != RebootMode.Never)
            {
                parts.Add("--reboot-delay");
                parts.Add(rebootDelay.ToString(CultureInfo.InvariantCulture));
            }
            string[] valueOptions = { "kb", "exclude-kb", "category", "severity", "title", "exclude-title", "id", "max-size-mb", "source", "criteria", "log" };
            foreach (var opt in valueOptions)
            {
                if (a.Has(opt))
                {
                    parts.Add("--" + opt);
                    parts.Add(Text.QuoteArgument(a.Get(opt)));
                }
            }
            foreach (var flag in new[] { "include-optional", "include-drivers", "no-log" })
            {
                if (a.Flag(flag)) parts.Add("--" + flag);
            }
            return Text.Join(" ", parts);
        }

        public void CreateSchedule(ScheduleDefinition def)
        {
            RequireAdmin();
            def.ExecutablePath = s.Environment.ExecutablePath;
            string lower = (def.ExecutablePath ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("\\downloads\\") || lower.Contains("\\temp\\") || lower.Contains("\\desktop\\"))
                ConsoleUi.Warn("The executable is in a temporary/user folder. Move it to a permanent folder (e.g. C:\\Tools\\WinUpdateManager) so the task keeps working.");

            s.Scheduler.CreateOrUpdate(def);
            ConsoleUi.Success("Scheduled task '" + def.TaskName + "' created.");
            ConsoleUi.KeyValue("Schedule", def.Describe());
            ConsoleUi.KeyValue("Runs as", "SYSTEM (highest privileges)");
            ConsoleUi.KeyValue("Command", Text.QuoteArgument(def.ExecutablePath) + " " + def.Arguments);
            ConsoleUi.KeyValue("Logs", "%ProgramData%\\WinUpdateManager\\logs");
        }

        public void ShowSchedule(string name)
        {
            var t = s.Scheduler.Get(name);
            if (json)
            {
                var w = new JsonWriter().BeginObject()
                    .Property("name", t.Name)
                    .Property("exists", t.Exists)
                    .Property("enabled", t.Enabled)
                    .Property("state", t.State)
                    .Property("schedule", t.Schedule)
                    .Property("lastRun", t.LastRun)
                    .Property("nextRun", t.NextRun)
                    .Property("lastResult", t.Exists ? Text.Hex(t.LastResult) : null)
                    .Property("executable", t.ExecutablePath)
                    .Property("arguments", t.Arguments)
                    .Property("runAs", t.RunAs)
                    .EndObject();
                ConsoleUi.Raw(w.ToString());
                return;
            }
            ConsoleUi.Header("Scheduled automatic installation");
            if (!t.Exists)
            {
                ConsoleUi.Info("  No scheduled task named '" + name + "'. Create one with: " + ExeName + " schedule create");
                return;
            }
            ConsoleUi.KeyValue("Task", t.Name);
            ConsoleUi.KeyValue("Enabled / state", Text.YesNo(t.Enabled) + " / " + t.State);
            ConsoleUi.KeyValue("Schedule", t.Schedule);
            ConsoleUi.KeyValue("Next run", Text.FormatDate(t.NextRun));
            ConsoleUi.KeyValue("Last run", Text.FormatDate(t.LastRun) + (t.LastRun.HasValue ? " (result " + Text.Hex(t.LastResult) + ")" : string.Empty));
            ConsoleUi.KeyValue("Runs as", t.RunAs);
            ConsoleUi.KeyValue("Command", Text.QuoteArgument(t.ExecutablePath) + " " + t.Arguments);
        }

        // ------------------------------------------------------------------ Microsoft Update

        private int CmdMicrosoftUpdate(ParsedArgs a)
        {
            string sub = (a.Positional(0) ?? "status").ToLowerInvariant();
            switch (sub)
            {
                case "status":
                    ShowUpdateServices();
                    return ExitCodes.Success;
                case "enable":
                    SetMicrosoftUpdate(true);
                    return ExitCodes.Success;
                case "disable":
                    SetMicrosoftUpdate(false);
                    return ExitCodes.Success;
                default:
                    throw new UsageException("Unknown microsoft-update action '" + sub + "'. Use: status, enable, disable.");
            }
        }

        public void ShowUpdateServices()
        {
            var list = s.Updates.GetUpdateServices();
            bool registered = list.Any(x => Text.EqualsIgnoreCase(x.ServiceId, WellKnownServices.MicrosoftUpdate));
            if (json)
            {
                var w = new JsonWriter().BeginObject().Property("microsoftUpdateRegistered", registered).BeginArray("services");
                foreach (var x in list)
                {
                    w.BeginObject().Property("name", x.Name).Property("serviceId", x.ServiceId).Property("isDefaultAUService", x.IsDefaultAUService)
                        .Property("isManaged", x.IsManaged).Property("isRegisteredWithAU", x.IsRegisteredWithAU).EndObject();
                }
                ConsoleUi.Raw(w.EndArray().EndObject().ToString());
                return;
            }
            ConsoleUi.Header("Update services");
            ConsoleUi.KeyValue("Microsoft Update", registered ? "Registered" : "Not registered (enable with: " + ExeName + " microsoft-update enable)");
            ConsoleUi.Line();
            ConsoleUi.Table(new[] { "Name", "Default AU", "Managed", "Service ID" },
                list.Select(x => new[] { x.Name, Text.YesNo(x.IsDefaultAUService), Text.YesNo(x.IsManaged), x.ServiceId }).ToList(), 0);
        }

        public void SetMicrosoftUpdate(bool enable)
        {
            RequireAdmin();
            if (enable)
            {
                s.Updates.RegisterMicrosoftUpdate();
                ConsoleUi.Success("Microsoft Update registered. Updates for other Microsoft products (SQL Server, .NET, Visual C++ ...) will be offered.");
            }
            else
            {
                s.Updates.UnregisterMicrosoftUpdate();
                ConsoleUi.Success("Microsoft Update unregistered. Only Windows updates will be offered.");
            }
        }
    }
}
