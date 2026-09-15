using System;
using System.Collections.Generic;

namespace WinUpdateManager.Core
{
    public enum UpdateKind
    {
        Software = 1,
        Driver = 2
    }

    /// <summary>Matches WUA OperationResultCode.</summary>
    public enum OperationResult
    {
        NotStarted = 0,
        InProgress = 1,
        Succeeded = 2,
        SucceededWithErrors = 3,
        Failed = 4,
        Aborted = 5,
        Skipped = 100
    }

    /// <summary>Matches WUA InstallationRebootBehavior.</summary>
    public enum RebootBehavior
    {
        NeverReboots = 0,
        AlwaysRequiresReboot = 1,
        CanRequestReboot = 2
    }

    public enum UpdateSource
    {
        /// <summary>Whatever the server is configured to use (WSUS if configured, otherwise Windows/Microsoft Update).</summary>
        Default,
        Wsus,
        WindowsUpdate,
        MicrosoftUpdate
    }

    public enum SearchScope
    {
        Available,
        Hidden,
        Installed
    }

    public enum RebootMode
    {
        Never,
        IfRequired,
        Always
    }

    public enum StartupMode
    {
        Automatic,
        Manual,
        Disabled
    }

    public enum ScheduleFrequency
    {
        Daily,
        Weekly
    }

    public static class WellKnownServices
    {
        public const string MicrosoftUpdate = "7971f918-a847-4430-9279-4a52d1efe18d";
        public const string WindowsUpdate = "9482f4b4-e343-43b6-b170-9a65bc822c77";
        public const string Wsus = "3da21691-e39d-4da6-8a4b-b43877bcb1b7";
    }

    public sealed class UpdateInfo
    {
        public UpdateInfo()
        {
            KbArticleIds = new List<string>();
            Classifications = new List<string>();
            Products = new List<string>();
            Kind = UpdateKind.Software;
        }

        public string Id { get; set; }
        public int Revision { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public List<string> KbArticleIds { get; private set; }
        public string Severity { get; set; }
        public List<string> Classifications { get; private set; }
        public List<string> Products { get; private set; }
        public decimal MaxDownloadSize { get; set; }
        public UpdateKind Kind { get; set; }
        public bool IsDownloaded { get; set; }
        public bool IsHidden { get; set; }
        public bool IsInstalled { get; set; }
        public bool IsMandatory { get; set; }

        /// <summary>WUA BrowseOnly: optional update (e.g. preview cumulative updates) not installed automatically.</summary>
        public bool IsOptional { get; set; }

        public bool EulaAccepted { get; set; }
        public bool IsUninstallable { get; set; }
        public bool RebootRequired { get; set; }
        public RebootBehavior RebootBehavior { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string SupportUrl { get; set; }

        /// <summary>Underlying Windows Update Agent COM object (IUpdate). Null in simulation.</summary>
        public object NativeHandle { get; set; }

        public string KbDisplay
        {
            get { return KbArticleIds.Count > 0 ? "KB" + KbArticleIds[0] : "-"; }
        }

        public string SeverityDisplay
        {
            get { return Text.IsBlank(Severity) ? "-" : Severity; }
        }

        public string ClassificationDisplay
        {
            get
            {
                if (Classifications.Count > 0) return Classifications[0];
                return Kind == UpdateKind.Driver ? "Drivers" : "-";
            }
        }

        public bool MayRequireReboot
        {
            get { return RebootRequired || RebootBehavior != RebootBehavior.NeverReboots; }
        }
    }

    public sealed class SearchOptions
    {
        public SearchOptions()
        {
            Source = UpdateSource.Default;
            Scope = SearchScope.Available;
        }

        public UpdateSource Source { get; set; }
        public SearchScope Scope { get; set; }
        public bool IncludeDrivers { get; set; }

        /// <summary>Raw WUA search criteria; overrides Scope/IncludeDrivers when set.</summary>
        public string RawCriteria { get; set; }
    }

    public sealed class UpdateActionResult
    {
        public UpdateInfo Update { get; set; }
        public string Action { get; set; }
        public OperationResult Result { get; set; }
        public int HResult { get; set; }
        public bool RebootRequired { get; set; }
        public string Message { get; set; }
        public TimeSpan Duration { get; set; }

        public bool IsSuccess
        {
            get { return Result == OperationResult.Succeeded || Result == OperationResult.SucceededWithErrors; }
        }
    }

    public sealed class HistoryEntry
    {
        public DateTime? Date { get; set; }
        public string Operation { get; set; }
        public OperationResult Result { get; set; }
        public int HResult { get; set; }
        public string Title { get; set; }
        public string UpdateId { get; set; }
        public string Kb { get; set; }
        public string ClientApplication { get; set; }
    }

    public sealed class AgentInfo
    {
        public string Version { get; set; }
        public DateTime? LastSearchSuccess { get; set; }
        public DateTime? LastInstallSuccess { get; set; }
        public bool RebootRequired { get; set; }
        public bool? AutomaticUpdatesEnabled { get; set; }
        public string AutomaticUpdatesSetting { get; set; }
    }

    public sealed class UpdateServiceInfo
    {
        public string Name { get; set; }
        public string ServiceId { get; set; }
        public bool IsDefaultAUService { get; set; }
        public bool IsManaged { get; set; }
        public bool IsRegisteredWithAU { get; set; }
    }

    public sealed class SystemInfo
    {
        public string ComputerName { get; set; }
        public string Fqdn { get; set; }
        public string ProductName { get; set; }
        public string Family { get; set; }
        public string EditionId { get; set; }
        public string InstallationType { get; set; }
        public string DisplayVersion { get; set; }
        public string ServicePack { get; set; }
        public int Build { get; set; }
        public int Ubr { get; set; }
        public bool IsServer { get; set; }
        public bool IsServerCore { get; set; }
        public bool IsDomainController { get; set; }
        public bool IsDomainJoined { get; set; }
        public string Architecture { get; set; }
        public string ClrVersion { get; set; }
        public DateTime? LastBoot { get; set; }
        public DateTime? ExtendedSupportEnd { get; set; }
        public bool IsElevated { get; set; }

        public string BuildDisplay
        {
            get { return Ubr > 0 ? Build + "." + Ubr : Build.ToString(); }
        }
    }

    public sealed class PendingRebootReason
    {
        public PendingRebootReason(string source, bool definite)
        {
            Source = source;
            Definite = definite;
        }

        public string Source { get; private set; }

        /// <summary>False for indicators that are often set by non-update software (e.g. PendingFileRenameOperations).</summary>
        public bool Definite { get; private set; }
    }

    public sealed class ServiceState
    {
        public string Name { get; set; }
        public string DisplayName { get; set; }
        public string Status { get; set; }
        public string StartMode { get; set; }
    }

    public sealed class ScheduleDefinition
    {
        public ScheduleDefinition()
        {
            TaskName = DefaultTaskName;
            Frequency = ScheduleFrequency.Weekly;
            Day = DayOfWeek.Sunday;
            Hour = 3;
        }

        public const string DefaultTaskName = "WinUpdateManager - Automatic Updates";

        public string TaskName { get; set; }
        public ScheduleFrequency Frequency { get; set; }
        public DayOfWeek Day { get; set; }
        public int Hour { get; set; }
        public int Minute { get; set; }
        public string ExecutablePath { get; set; }
        public string Arguments { get; set; }

        public string Describe()
        {
            string time = Hour.ToString("00") + ":" + Minute.ToString("00");
            return Frequency == ScheduleFrequency.Daily
                ? "Every day at " + time
                : "Every " + Day + " at " + time;
        }
    }

    public sealed class ScheduledTaskInfo
    {
        public string Name { get; set; }
        public bool Exists { get; set; }
        public bool Enabled { get; set; }
        public string State { get; set; }
        public string Schedule { get; set; }
        public DateTime? LastRun { get; set; }
        public DateTime? NextRun { get; set; }
        public int LastResult { get; set; }
        public string ExecutablePath { get; set; }
        public string Arguments { get; set; }
        public string RunAs { get; set; }
    }
}
