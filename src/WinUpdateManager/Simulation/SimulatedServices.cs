using System;
using System.Collections.Generic;
using System.Linq;
using WinUpdateManager.Core;
using WinUpdateManager.Output;

namespace WinUpdateManager.Simulation
{
    public sealed class SimulatedUpdateProvider : IUpdateProvider
    {
        private readonly SimulationState st;

        public SimulatedUpdateProvider(SimulationState state)
        {
            st = state;
        }

        public IList<UpdateInfo> Search(SearchOptions options)
        {
            SimulationState.Pause(6);
            st.LastSearch = DateTime.Now;
            bool includeMu = st.MicrosoftUpdateRegistered || options.Source == UpdateSource.MicrosoftUpdate;
            if (options.Source == UpdateSource.Wsus && !st.Policy.UsesWsus)
                throw new ApiException("Search failed: no WSUS server is configured.", unchecked((int)0x8024002B));

            var result = new List<UpdateInfo>();
            foreach (var u in st.Catalog)
            {
                if (!options.IncludeDrivers && u.Kind == UpdateKind.Driver) continue;
                if (st.MicrosoftUpdateOnly.Contains(u.Id) && !includeMu) continue;
                switch (options.Scope)
                {
                    case SearchScope.Available:
                        if (u.IsInstalled || u.IsHidden) continue;
                        break;
                    case SearchScope.Hidden:
                        if (u.IsInstalled || !u.IsHidden) continue;
                        break;
                    case SearchScope.Installed:
                        if (!u.IsInstalled) continue;
                        break;
                }
                result.Add(u);
            }
            return result;
        }

        public void AcceptEula(UpdateInfo update)
        {
            update.EulaAccepted = true;
        }

        public UpdateActionResult Download(UpdateInfo update)
        {
            SimulationState.Pause(2 + (int)(update.MaxDownloadSize / (300m * 1024 * 1024)));
            update.IsDownloaded = true;
            return new UpdateActionResult { Update = update, Action = "download", Result = OperationResult.Succeeded };
        }

        public UpdateActionResult Install(UpdateInfo update)
        {
            SimulationState.Pause(4);
            var entry = new HistoryEntry
            {
                Date = DateTime.Now,
                Operation = "Installation",
                Title = update.Title,
                Kb = update.KbArticleIds.Count > 0 ? update.KbArticleIds[0] : null,
                UpdateId = update.Id,
                ClientApplication = "WinUpdateManager"
            };

            if (st.FailOnFirstInstall.Remove(update.Id))
            {
                entry.Result = OperationResult.Failed;
                entry.HResult = unchecked((int)0x80070643);
                st.History.Add(entry);
                return new UpdateActionResult
                {
                    Update = update,
                    Action = "install",
                    Result = OperationResult.Failed,
                    HResult = entry.HResult,
                    Message = "Simulated failure (succeeds when retried)"
                };
            }

            update.IsInstalled = true;
            update.IsUninstallable = true;
            bool reboot = update.RebootBehavior != RebootBehavior.NeverReboots;
            update.RebootRequired = reboot;
            if (reboot) st.RebootPending = true;
            st.LastInstall = DateTime.Now;
            entry.Result = OperationResult.Succeeded;
            st.History.Add(entry);
            return new UpdateActionResult { Update = update, Action = "install", Result = OperationResult.Succeeded, RebootRequired = reboot };
        }

        public UpdateActionResult Uninstall(UpdateInfo update)
        {
            SimulationState.Pause(4);
            update.IsInstalled = false;
            update.RebootRequired = true;
            st.RebootPending = true;
            st.History.Add(new HistoryEntry
            {
                Date = DateTime.Now,
                Operation = "Uninstallation",
                Result = OperationResult.Succeeded,
                Title = update.Title,
                Kb = update.KbArticleIds.Count > 0 ? update.KbArticleIds[0] : null,
                UpdateId = update.Id,
                ClientApplication = "WinUpdateManager"
            });
            return new UpdateActionResult { Update = update, Action = "uninstall", Result = OperationResult.Succeeded, RebootRequired = true };
        }

        public void SetHidden(UpdateInfo update, bool hidden)
        {
            update.IsHidden = hidden;
        }

        public IList<HistoryEntry> GetHistory(int count)
        {
            return st.History.OrderByDescending(h => h.Date).Take(count).ToList();
        }

        public AgentInfo GetAgentInfo()
        {
            return new AgentInfo
            {
                Version = "10.0.20348.4052 (simulated)",
                LastSearchSuccess = st.LastSearch,
                LastInstallSuccess = st.LastInstall,
                RebootRequired = st.RebootPending,
                AutomaticUpdatesEnabled = st.Policy.NoAutoUpdate != 1,
                AutomaticUpdatesSetting = st.Policy.DescribeMode()
            };
        }

        public bool IsInstallerBusy()
        {
            return false;
        }

        public bool IsRebootRequiredBeforeInstallation()
        {
            return false;
        }

        public IList<UpdateServiceInfo> GetUpdateServices()
        {
            var list = new List<UpdateServiceInfo>();
            list.Add(new UpdateServiceInfo { Name = "Windows Update", ServiceId = WellKnownServices.WindowsUpdate, IsDefaultAUService = !st.MicrosoftUpdateRegistered && !st.Policy.UsesWsus, IsRegisteredWithAU = true });
            if (st.MicrosoftUpdateRegistered)
                list.Add(new UpdateServiceInfo { Name = "Microsoft Update", ServiceId = WellKnownServices.MicrosoftUpdate, IsDefaultAUService = !st.Policy.UsesWsus, IsRegisteredWithAU = true });
            if (st.Policy.UsesWsus)
                list.Add(new UpdateServiceInfo { Name = "Windows Server Update Service", ServiceId = WellKnownServices.Wsus, IsDefaultAUService = true, IsManaged = true, IsRegisteredWithAU = true });
            return list;
        }

        public void RegisterMicrosoftUpdate()
        {
            SimulationState.Pause(2);
            st.MicrosoftUpdateRegistered = true;
        }

        public void UnregisterMicrosoftUpdate()
        {
            SimulationState.Pause(1);
            st.MicrosoftUpdateRegistered = false;
        }
    }

    public sealed class SimulatedEnvironment : ISystemEnvironment
    {
        private readonly SimulationState st;

        public SimulatedEnvironment(SimulationState state)
        {
            st = state;
        }

        public SystemInfo GetSystemInfo()
        {
            return new SystemInfo
            {
                ComputerName = "SIM-SRV01",
                Fqdn = "sim-srv01.contoso.local",
                ProductName = "Windows Server 2022 Datacenter (simulated)",
                Family = OsCatalog.GetFamily(20348, true),
                EditionId = "ServerDatacenter",
                InstallationType = "Server",
                DisplayVersion = "21H2",
                Build = 20348,
                Ubr = 4052,
                IsServer = true,
                IsDomainJoined = true,
                Architecture = "AMD64",
                ClrVersion = Environment.Version.ToString(),
                LastBoot = DateTime.Now.AddDays(-12).AddHours(-3),
                ExtendedSupportEnd = OsCatalog.GetExtendedSupportEnd(20348, true),
                IsElevated = true
            };
        }

        public List<PendingRebootReason> GetPendingRebootReasons()
        {
            var list = new List<PendingRebootReason>();
            if (st.RebootPending)
                list.Add(new PendingRebootReason("Windows Update: Auto Update\\RebootRequired (simulated)", true));
            return list;
        }

        public bool IsAdministrator
        {
            get { return true; }
        }

        public string ExecutablePath
        {
            get { return @"C:\Tools\WinUpdateManager\WinUpdateManager.exe"; }
        }

        public bool RelaunchElevated(string arguments)
        {
            return false;
        }
    }

    public sealed class SimulatedPolicyStore : IPolicyStore
    {
        private readonly SimulationState st;

        public SimulatedPolicyStore(SimulationState state)
        {
            st = state;
        }

        public AutoUpdatePolicy Read()
        {
            return st.Policy.Clone();
        }

        public void Write(AutoUpdatePolicy policy)
        {
            st.Policy = policy.Clone();
        }
    }

    public sealed class SimulatedServiceControl : IServiceControl
    {
        private readonly SimulationState st;

        public SimulatedServiceControl(SimulationState state)
        {
            st = state;
        }

        public IList<ServiceState> GetUpdateServices()
        {
            return st.Services.Values.ToList();
        }

        public ServiceState Get(string name)
        {
            ServiceState svc;
            if (!st.Services.TryGetValue(name, out svc))
                throw new InvalidOperationException("Service '" + name + "' was not found on this computer.");
            return svc;
        }

        public void Start(string name)
        {
            var svc = Get(name);
            if (svc.StartMode == "Disabled")
                throw new InvalidOperationException("Service '" + name + "' is disabled. Set its startup type first: services startup " + name + " manual");
            SimulationState.Pause(2);
            svc.Status = "Running";
        }

        public void Stop(string name)
        {
            var svc = Get(name);
            SimulationState.Pause(2);
            svc.Status = "Stopped";
        }

        public void SetStartupMode(string name, StartupMode mode)
        {
            Get(name).StartMode = mode.ToString();
        }

        public void ResetUpdateComponents(Action<string> progress)
        {
            foreach (var name in new[] { "wuauserv", "usosvc", "dosvc", "bits", "cryptsvc" })
            {
                progress("Stopping " + name + "...");
                Stop(name);
            }
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            progress(@"Renamed C:\Windows\SoftwareDistribution -> SoftwareDistribution.bak-" + stamp + " (simulated)");
            SimulationState.Pause(2);
            progress(@"Renamed C:\Windows\System32\catroot2 -> catroot2.bak-" + stamp + " (simulated)");
            progress("Cleared the BITS download queue (simulated)");
            foreach (var name in new[] { "cryptsvc", "bits", "wuauserv" })
            {
                progress("Starting " + name + "...");
                Start(name);
            }
        }
    }

    public sealed class SimulatedTaskScheduler : ITaskSchedulerService
    {
        private readonly SimulationState st;

        public SimulatedTaskScheduler(SimulationState state)
        {
            st = state;
        }

        public void CreateOrUpdate(ScheduleDefinition definition)
        {
            st.Tasks[definition.TaskName] = new ScheduledTaskInfo
            {
                Name = definition.TaskName,
                Exists = true,
                Enabled = true,
                State = "Ready",
                Schedule = definition.Describe(),
                NextRun = NextRun(definition, DateTime.Now),
                ExecutablePath = definition.ExecutablePath,
                Arguments = definition.Arguments,
                RunAs = "SYSTEM"
            };
        }

        public bool Delete(string taskName)
        {
            return st.Tasks.Remove(taskName);
        }

        public ScheduledTaskInfo Get(string taskName)
        {
            ScheduledTaskInfo info;
            return st.Tasks.TryGetValue(taskName, out info) ? info : new ScheduledTaskInfo { Name = taskName, Exists = false };
        }

        public static DateTime NextRun(ScheduleDefinition d, DateTime now)
        {
            var candidate = now.Date.AddHours(d.Hour).AddMinutes(d.Minute);
            if (d.Frequency == ScheduleFrequency.Daily)
                return candidate > now ? candidate : candidate.AddDays(1);

            int days = ((int)d.Day - (int)now.DayOfWeek + 7) % 7;
            candidate = candidate.AddDays(days);
            return candidate > now ? candidate : candidate.AddDays(7);
        }
    }

    public sealed class SimulatedPowerControl : IPowerControl
    {
        private readonly SimulationState st;

        public SimulatedPowerControl(SimulationState state)
        {
            st = state;
        }

        public void ScheduleRestart(int delaySeconds, string message)
        {
            st.RestartScheduled = true;
            ConsoleUi.Detail("[simulation] A real server would now restart in " + delaySeconds + " seconds.");
        }

        public void AbortRestart()
        {
            if (!st.RestartScheduled)
                throw new ApiException("No restart is scheduled.", unchecked((int)0x8007045C));
            st.RestartScheduled = false;
        }
    }
}
