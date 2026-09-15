using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using WinUpdateManager.Core;

namespace WinUpdateManager.Simulation
{
    /// <summary>
    /// In-memory "virtual Windows Server 2022" used by --simulate so the tool can be tried safely
    /// (and tested on machines without Windows Update).
    /// </summary>
    public sealed class SimulationState
    {
        /// <summary>Base delay used to make operations feel realistic. Tests set this to 0.</summary>
        public static int DelayMs = 300;

        public SimulationState()
        {
            Catalog = new List<UpdateInfo>();
            History = new List<HistoryEntry>();
            MicrosoftUpdateOnly = new HashSet<string>();
            FailOnFirstInstall = new HashSet<string>();
            Policy = new AutoUpdatePolicy();
            Services = new Dictionary<string, ServiceState>(StringComparer.OrdinalIgnoreCase);
            Tasks = new Dictionary<string, ScheduledTaskInfo>(StringComparer.OrdinalIgnoreCase);
        }

        public List<UpdateInfo> Catalog { get; private set; }
        public List<HistoryEntry> History { get; private set; }
        public HashSet<string> MicrosoftUpdateOnly { get; private set; }
        public HashSet<string> FailOnFirstInstall { get; private set; }
        public AutoUpdatePolicy Policy { get; set; }
        public Dictionary<string, ServiceState> Services { get; private set; }
        public Dictionary<string, ScheduledTaskInfo> Tasks { get; private set; }
        public bool RebootPending { get; set; }
        public bool RestartScheduled { get; set; }
        public bool MicrosoftUpdateRegistered { get; set; }
        public DateTime? LastSearch { get; set; }
        public DateTime? LastInstall { get; set; }

        public static void Pause(int units)
        {
            if (DelayMs > 0 && units > 0) Thread.Sleep(DelayMs * units);
        }

        public static AppServices CreateServices()
        {
            var state = CreateDefault();
            return new AppServices
            {
                IsSimulation = true,
                Updates = new SimulatedUpdateProvider(state),
                Environment = new SimulatedEnvironment(state),
                Policy = new SimulatedPolicyStore(state),
                Services = new SimulatedServiceControl(state),
                Scheduler = new SimulatedTaskScheduler(state),
                Power = new SimulatedPowerControl(state)
            };
        }

        public static SimulationState CreateDefault()
        {
            var st = new SimulationState();
            var today = DateTime.Today;
            string month = today.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            string previous = today.AddMonths(-1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
            const string os = "Microsoft server operating system version 21H2 for x64-based Systems";
            int n = 0;

            st.Catalog.Add(Make(++n, "5065432", month + " Cumulative Update for " + os + " (KB5065432)", "Security Updates", "Critical", 812, RebootBehavior.CanRequestReboot, today.AddDays(-6)));
            st.Catalog.Add(Make(++n, "5064401", month + " Cumulative Update for .NET Framework 3.5, 4.8 and 4.8.1 for " + os + " (KB5064401)", "Security Updates", "Important", 74, RebootBehavior.CanRequestReboot, today.AddDays(-6)));
            st.Catalog.Add(Make(++n, "2267602", "Security Intelligence Update for Microsoft Defender Antivirus - KB2267602 (Version 1.439.212.0) - Current Channel (Broad)", "Definition Updates", null, 118, RebootBehavior.NeverReboots, today));
            st.Catalog.Add(Make(++n, "890830", "Windows Malicious Software Removal Tool x64 - v5.143 (KB890830)", "Update Rollups", null, 72, RebootBehavior.NeverReboots, today.AddDays(-6)));

            var flaky = Make(++n, "5062222", "Update for " + os + " (KB5062222)", "Critical Updates", "Important", 18, RebootBehavior.CanRequestReboot, today.AddDays(-20));
            st.Catalog.Add(flaky);
            st.FailOnFirstInstall.Add(flaky.Id);

            var preview = Make(++n, "5066789", month + " Cumulative Update Preview for " + os + " (KB5066789)", "Updates", null, 790, RebootBehavior.CanRequestReboot, today.AddDays(-2));
            preview.IsOptional = true;
            st.Catalog.Add(preview);

            var driver = Make(++n, null, "Intel Corporation - System - 10.1.19444.8378", "Drivers", null, 1, RebootBehavior.CanRequestReboot, today.AddDays(-40));
            driver.Kind = UpdateKind.Driver;
            driver.Classifications.Clear();
            st.Catalog.Add(driver);

            var sql = Make(++n, "5054833", "Cumulative Update 32 for SQL Server 2019 (KB5054833)", "Updates", null, 590, RebootBehavior.CanRequestReboot, today.AddDays(-25));
            sql.Products.Clear();
            sql.Products.Add("Microsoft SQL Server 2019");
            st.Catalog.Add(sql);
            st.MicrosoftUpdateOnly.Add(sql.Id);

            var hidden = Make(++n, "5062555", previous + " Cumulative Update Preview for " + os + " (KB5062555)", "Updates", null, 770, RebootBehavior.CanRequestReboot, today.AddDays(-33));
            hidden.IsOptional = true;
            hidden.IsHidden = true;
            st.Catalog.Add(hidden);

            var installedCu = Make(++n, "5063880", previous + " Cumulative Update for " + os + " (KB5063880)", "Security Updates", "Critical", 790, RebootBehavior.CanRequestReboot, today.AddDays(-36));
            installedCu.IsInstalled = true;
            installedCu.IsDownloaded = true;
            installedCu.IsUninstallable = false;
            st.Catalog.Add(installedCu);

            var installedNet = Make(++n, "5062001", previous + " Cumulative Update for .NET Framework 3.5, 4.8 and 4.8.1 for " + os + " (KB5062001)", "Security Updates", "Important", 70, RebootBehavior.CanRequestReboot, today.AddDays(-36));
            installedNet.IsInstalled = true;
            installedNet.IsDownloaded = true;
            installedNet.IsUninstallable = true;
            st.Catalog.Add(installedNet);

            var now = DateTime.Now;
            st.History.Add(new HistoryEntry { Date = now.AddDays(-30).AddHours(-2), Operation = "Installation", Result = OperationResult.Succeeded, Title = installedCu.Title, Kb = "5063880", UpdateId = installedCu.Id, ClientApplication = "UpdateOrchestrator" });
            st.History.Add(new HistoryEntry { Date = now.AddDays(-30).AddHours(-3), Operation = "Installation", Result = OperationResult.Succeeded, Title = installedNet.Title, Kb = "5062001", UpdateId = installedNet.Id, ClientApplication = "UpdateOrchestrator" });
            st.History.Add(new HistoryEntry { Date = now.AddDays(-31), Operation = "Installation", Result = OperationResult.Failed, HResult = unchecked((int)0x80070643), Title = flaky.Title, Kb = "5062222", UpdateId = flaky.Id, ClientApplication = "UpdateOrchestrator" });
            st.History.Add(new HistoryEntry { Date = now.AddDays(-33), Operation = "Installation", Result = OperationResult.Succeeded, Title = "Security Intelligence Update for Microsoft Defender Antivirus - KB2267602 (Version 1.437.101.0)", Kb = "2267602", ClientApplication = "Windows Defender" });

            st.LastSearch = now.AddHours(-9);
            st.LastInstall = now.AddDays(-30).AddHours(-2);
            st.Policy = new AutoUpdatePolicy { NoAutoUpdate = 0, AUOptions = 3 };

            AddService(st, "wuauserv", "Windows Update", "Running", "Manual");
            AddService(st, "bits", "Background Intelligent Transfer Service", "Running", "Manual");
            AddService(st, "cryptsvc", "Cryptographic Services", "Running", "Automatic");
            AddService(st, "msiserver", "Windows Installer", "Stopped", "Manual");
            AddService(st, "trustedinstaller", "Windows Modules Installer", "Stopped", "Manual");
            AddService(st, "usosvc", "Update Orchestrator Service", "Running", "Manual");
            AddService(st, "dosvc", "Delivery Optimization", "Running", "Automatic (Delayed)");
            return st;
        }

        private static UpdateInfo Make(int n, string kb, string title, string classification, string severity, decimal sizeMb, RebootBehavior reboot, DateTime released)
        {
            var u = new UpdateInfo
            {
                Id = string.Format(CultureInfo.InvariantCulture, "5a1e0000-0000-4000-8000-{0:000000000000}", n),
                Revision = 200 + n,
                Title = title,
                Description = "Simulated update: " + title,
                Severity = severity,
                MaxDownloadSize = sizeMb * 1024m * 1024m,
                RebootBehavior = reboot,
                ReleaseDate = released,
                EulaAccepted = true,
                SupportUrl = kb != null ? "https://support.microsoft.com/help/" + kb : null
            };
            if (kb != null) u.KbArticleIds.Add(kb);
            if (classification != null) u.Classifications.Add(classification);
            u.Products.Add("Windows Server 2022");
            return u;
        }

        private static void AddService(SimulationState st, string name, string display, string status, string mode)
        {
            st.Services[name] = new ServiceState { Name = name, DisplayName = display, Status = status, StartMode = mode };
        }
    }
}
