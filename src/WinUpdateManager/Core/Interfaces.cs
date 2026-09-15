using System;
using System.Collections.Generic;

namespace WinUpdateManager.Core
{
    /// <summary>Windows Update Agent operations.</summary>
    public interface IUpdateProvider
    {
        IList<UpdateInfo> Search(SearchOptions options);
        void AcceptEula(UpdateInfo update);
        UpdateActionResult Download(UpdateInfo update);
        UpdateActionResult Install(UpdateInfo update);
        UpdateActionResult Uninstall(UpdateInfo update);
        void SetHidden(UpdateInfo update, bool hidden);
        IList<HistoryEntry> GetHistory(int count);
        AgentInfo GetAgentInfo();
        bool IsInstallerBusy();
        bool IsRebootRequiredBeforeInstallation();
        IList<UpdateServiceInfo> GetUpdateServices();
        void RegisterMicrosoftUpdate();
        void UnregisterMicrosoftUpdate();
    }

    /// <summary>Operating system information.</summary>
    public interface ISystemEnvironment
    {
        SystemInfo GetSystemInfo();
        List<PendingRebootReason> GetPendingRebootReasons();
        bool IsAdministrator { get; }
        string ExecutablePath { get; }
        bool RelaunchElevated(string arguments);
    }

    /// <summary>Windows Update group-policy registry settings (HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate).</summary>
    public interface IPolicyStore
    {
        AutoUpdatePolicy Read();
        void Write(AutoUpdatePolicy policy);
    }

    /// <summary>Windows services used by Windows Update.</summary>
    public interface IServiceControl
    {
        IList<ServiceState> GetUpdateServices();
        ServiceState Get(string name);
        void Start(string name);
        void Stop(string name);
        void SetStartupMode(string name, StartupMode mode);
        void ResetUpdateComponents(Action<string> progress);
    }

    public interface ITaskSchedulerService
    {
        void CreateOrUpdate(ScheduleDefinition definition);
        bool Delete(string taskName);
        ScheduledTaskInfo Get(string taskName);
    }

    public interface IPowerControl
    {
        void ScheduleRestart(int delaySeconds, string message);
        void AbortRestart();
    }
}
