using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using WinUpdateManager.Core;

namespace WinUpdateManager.Platform
{
    /// <summary>Task Scheduler 2.0 COM API (Schedule.Service), available on Windows Server 2008 and later.</summary>
    internal sealed class WindowsTaskScheduler : ITaskSchedulerService
    {
        private const int TriggerDaily = 2;
        private const int TriggerWeekly = 3;
        private const int ActionExec = 0;
        private const int CreateOrUpdateFlag = 6;
        private const int LogonServiceAccount = 5;
        private const int RunLevelHighest = 1;
        private const int InstancesIgnoreNew = 2;

        public void CreateOrUpdate(ScheduleDefinition d)
        {
            var service = Connect();
            var definition = Com.Call(service, "NewTask", 0);

            var registration = Com.Get(definition, "RegistrationInfo");
            Com.Set(registration, "Description", "Installs Windows updates using WinUpdateManager (" + d.Describe() + ").");
            Com.Set(registration, "Author", "WinUpdateManager");

            var principal = Com.Get(definition, "Principal");
            Com.Set(principal, "LogonType", LogonServiceAccount);
            Com.Set(principal, "UserId", "SYSTEM");
            Com.Set(principal, "RunLevel", RunLevelHighest);

            var settings = Com.Get(definition, "Settings");
            Com.Set(settings, "Enabled", true);
            Com.Set(settings, "StartWhenAvailable", true);
            Com.Set(settings, "DisallowStartIfOnBatteries", false);
            Com.Set(settings, "StopIfGoingOnBatteries", false);
            Com.Set(settings, "ExecutionTimeLimit", "PT8H");
            Com.Set(settings, "MultipleInstances", InstancesIgnoreNew);

            var triggers = Com.Get(definition, "Triggers");
            var start = DateTime.Today.AddHours(d.Hour).AddMinutes(d.Minute);
            if (d.Frequency == ScheduleFrequency.Daily)
            {
                var trigger = Com.Call(triggers, "Create", TriggerDaily);
                Com.Set(trigger, "StartBoundary", start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
                Com.Set(trigger, "DaysInterval", (short)1);
                Com.Set(trigger, "Enabled", true);
            }
            else
            {
                var trigger = Com.Call(triggers, "Create", TriggerWeekly);
                Com.Set(trigger, "StartBoundary", start.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture));
                Com.Set(trigger, "DaysOfWeek", (short)(1 << (int)d.Day));
                Com.Set(trigger, "WeeksInterval", (short)1);
                Com.Set(trigger, "Enabled", true);
            }

            var actions = Com.Get(definition, "Actions");
            var action = Com.Call(actions, "Create", ActionExec);
            Com.Set(action, "Path", d.ExecutablePath);
            Com.Set(action, "Arguments", d.Arguments);
            Com.Set(action, "WorkingDirectory", Path.GetDirectoryName(d.ExecutablePath));

            var folder = Com.Call(service, "GetFolder", "\\");
            Com.Call(folder, "RegisterTaskDefinition", d.TaskName, definition, CreateOrUpdateFlag, "SYSTEM", null, LogonServiceAccount);
        }

        public bool Delete(string taskName)
        {
            var folder = Com.Call(Connect(), "GetFolder", "\\");
            try
            {
                Com.Call(folder, "DeleteTask", taskName, 0);
                return true;
            }
            catch (ApiException ex)
            {
                if (IsNotFound(ex.ErrorCode)) return false;
                throw;
            }
        }

        public ScheduledTaskInfo Get(string taskName)
        {
            var folder = Com.Call(Connect(), "GetFolder", "\\");
            object task;
            try
            {
                task = Com.Call(folder, "GetTask", taskName);
            }
            catch (ApiException ex)
            {
                if (IsNotFound(ex.ErrorCode)) return new ScheduledTaskInfo { Name = taskName, Exists = false };
                throw;
            }

            var info = new ScheduledTaskInfo
            {
                Name = taskName,
                Exists = true,
                Enabled = Com.GetBool(task, "Enabled"),
                State = StateName(Com.GetInt(task, "State")),
                LastRun = Com.GetDate(task, "LastRunTime"),
                NextRun = Com.GetDate(task, "NextRunTime"),
                LastResult = Com.GetInt(task, "LastTaskResult")
            };

            var definition = Com.TryGet(task, "Definition");
            if (definition != null)
            {
                var principal = Com.TryGet(definition, "Principal");
                info.RunAs = principal != null ? Com.GetString(principal, "UserId") : null;

                var actions = Com.TryGet(definition, "Actions");
                if (actions != null && Com.GetInt(actions, "Count") > 0)
                {
                    var action = Com.Get(actions, "Item", 1); // Task Scheduler collections are 1-based
                    info.ExecutablePath = Com.GetString(action, "Path");
                    info.Arguments = Com.GetString(action, "Arguments");
                }

                var triggers = Com.TryGet(definition, "Triggers");
                if (triggers != null && Com.GetInt(triggers, "Count") > 0)
                {
                    info.Schedule = DescribeTrigger(Com.Get(triggers, "Item", 1));
                }
            }
            return info;
        }

        private static object Connect()
        {
            var service = Com.Create("Schedule.Service");
            Com.Call(service, "Connect");
            return service;
        }

        private static string DescribeTrigger(object trigger)
        {
            int type = Com.GetInt(trigger, "Type");
            string boundary = Com.GetString(trigger, "StartBoundary") ?? string.Empty;
            string time = boundary.Length >= 16 ? boundary.Substring(11, 5) : boundary;
            if (type == TriggerDaily) return "Every day at " + time;
            if (type == TriggerWeekly)
            {
                int mask = Com.GetInt(trigger, "DaysOfWeek");
                var days = new List<string>();
                for (int i = 0; i < 7; i++)
                {
                    if ((mask & (1 << i)) != 0) days.Add(((DayOfWeek)i).ToString());
                }
                return "Every " + Text.Join(", ", days) + " at " + time;
            }
            return "Trigger type " + type + " starting " + boundary;
        }

        private static string StateName(int state)
        {
            switch (state)
            {
                case 1: return "Disabled";
                case 2: return "Queued";
                case 3: return "Ready";
                case 4: return "Running";
                default: return "Unknown";
            }
        }

        private static bool IsNotFound(int code)
        {
            return code == unchecked((int)0x80070002) || code == unchecked((int)0x80070003);
        }
    }

    internal sealed class WindowsPowerControl : IPowerControl
    {
        private const uint TokenAdjustPrivileges = 0x0020;
        private const uint TokenQuery = 0x0008;
        private const uint SePrivilegeEnabled = 0x0002;
        // SHTDN_REASON_FLAG_PLANNED | SHTDN_REASON_MAJOR_OPERATINGSYSTEM | SHTDN_REASON_MINOR_HOTFIX
        private const uint ShutdownReason = 0x80000000 | 0x00020000 | 0x00000011;

        [StructLayout(LayoutKind.Sequential)]
        private struct Luid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenPrivileges
        {
            public uint PrivilegeCount;
            public Luid Luid;
            public uint Attributes;
        }

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string systemName, string name, out Luid luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TokenPrivileges newState, uint length, IntPtr previous, IntPtr returnLength);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool InitiateSystemShutdownEx(string machineName, string message, uint timeout, bool forceAppsClosed, bool rebootAfterShutdown, uint reason);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool AbortSystemShutdown(string machineName);

        public void ScheduleRestart(int delaySeconds, string message)
        {
            EnableShutdownPrivilege();
            if (!InitiateSystemShutdownEx(null, message, (uint)Math.Max(0, delaySeconds), true, true, ShutdownReason))
                throw LastError("Scheduling the restart");
        }

        public void AbortRestart()
        {
            EnableShutdownPrivilege();
            if (!AbortSystemShutdown(null))
            {
                int error = Marshal.GetLastWin32Error();
                if (error == 1116) throw new ApiException("No restart is scheduled.", unchecked((int)0x8007045C));
                throw LastError("Cancelling the restart", error);
            }
        }

        private static void EnableShutdownPrivilege()
        {
            IntPtr token;
            if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out token))
                throw LastError("OpenProcessToken");
            try
            {
                Luid luid;
                if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out luid))
                    throw LastError("LookupPrivilegeValue");
                var tp = new TokenPrivileges { PrivilegeCount = 1, Luid = luid, Attributes = SePrivilegeEnabled };
                if (!AdjustTokenPrivileges(token, false, ref tp, (uint)Marshal.SizeOf(typeof(TokenPrivileges)), IntPtr.Zero, IntPtr.Zero))
                    throw LastError("AdjustTokenPrivileges");
                if (Marshal.GetLastWin32Error() == 1300) // ERROR_NOT_ALL_ASSIGNED
                    throw new NotElevatedException();
            }
            finally
            {
                CloseHandle(token);
            }
        }

        private static ApiException LastError(string operation)
        {
            return LastError(operation, Marshal.GetLastWin32Error());
        }

        private static ApiException LastError(string operation, int error)
        {
            return new ApiException(operation + " failed: " + new Win32Exception(error).Message, unchecked((int)(0x80070000 | (uint)error)));
        }
    }
}
