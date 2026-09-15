using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using WinUpdateManager.Core;

namespace WinUpdateManager.Platform
{
    internal static class WindowsPlatform
    {
        public static AppServices CreateServices()
        {
            return new AppServices
            {
                IsSimulation = false,
                Updates = new WuaUpdateProvider(),
                Environment = new WindowsEnvironment(),
                Policy = new RegistryPolicyStore(),
                Services = new WindowsServiceControl(),
                Scheduler = new WindowsTaskScheduler(),
                Power = new WindowsPowerControl()
            };
        }
    }

    internal sealed class WindowsEnvironment : ISystemEnvironment
    {
        private bool? isAdmin;

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetGetJoinInformation(string server, out IntPtr domainName, out int status);

        [DllImport("netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr buffer);

        public bool IsAdministrator
        {
            get
            {
                if (!isAdmin.HasValue)
                {
                    try
                    {
                        isAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
                    }
                    catch (Exception)
                    {
                        isAdmin = false;
                    }
                }
                return isAdmin.Value;
            }
        }

        public string ExecutablePath
        {
            get
            {
                var entry = Assembly.GetEntryAssembly();
                if (entry != null && !Text.IsBlank(entry.Location)) return entry.Location;
                return Process.GetCurrentProcess().MainModule.FileName;
            }
        }

        public bool RelaunchElevated(string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo(ExecutablePath, arguments) { UseShellExecute = true, Verb = "runas" };
                Process.Start(psi);
                return true;
            }
            catch (Win32Exception)
            {
                return false;
            }
        }

        public SystemInfo GetSystemInfo()
        {
            var info = new SystemInfo { ComputerName = Environment.MachineName };

            using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                if (k != null)
                {
                    info.ProductName = k.GetValue("ProductName") as string;
                    info.EditionId = k.GetValue("EditionID") as string;
                    info.InstallationType = k.GetValue("InstallationType") as string;
                    info.DisplayVersion = (k.GetValue("DisplayVersion") as string) ?? (k.GetValue("ReleaseId") as string);
                    info.ServicePack = k.GetValue("CSDVersion") as string;
                    int build;
                    if (int.TryParse((k.GetValue("CurrentBuildNumber") as string) ?? (k.GetValue("CurrentBuild") as string), out build))
                        info.Build = build;
                    var ubr = k.GetValue("UBR");
                    if (ubr is int) info.Ubr = (int)ubr;
                }
            }
            if (info.Build == 0) info.Build = Environment.OSVersion.Version.Build;

            string productType = null;
            using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ProductOptions"))
            {
                if (k != null) productType = k.GetValue("ProductType") as string;
            }
            info.IsServer = productType == null ? Text.ContainsIgnoreCase(info.ProductName, "Server") : !Text.EqualsIgnoreCase(productType, "WinNT");
            info.IsDomainController = Text.EqualsIgnoreCase(productType, "LanmanNT");
            info.IsServerCore = Text.EqualsIgnoreCase(info.InstallationType, "Server Core") || Text.EqualsIgnoreCase(info.InstallationType, "Nano Server");
            info.Family = OsCatalog.GetFamily(info.Build, info.IsServer);
            info.ExtendedSupportEnd = OsCatalog.GetExtendedSupportEnd(info.Build, info.IsServer);

            // Windows Server 2025 still reports some legacy values; prefer the build based family name.
            if (Text.IsBlank(info.ProductName)) info.ProductName = info.Family;

            info.Architecture = Environment.GetEnvironmentVariable("PROCESSOR_ARCHITEW6432") ??
                                Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE");
            info.ClrVersion = Environment.Version.ToString();
            info.IsElevated = IsAdministrator;

            try
            {
                info.LastBoot = DateTime.Now.AddMilliseconds(-(double)GetTickCount64());
            }
            catch (EntryPointNotFoundException)
            {
                // pre-Vista only
            }

            try
            {
                IntPtr name;
                int status;
                if (NetGetJoinInformation(null, out name, out status) == 0)
                {
                    if (status == 3) // NetSetupDomainName
                    {
                        info.IsDomainJoined = true;
                        string domain = Marshal.PtrToStringUni(name);
                        using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters"))
                        {
                            string dns = k != null ? k.GetValue("Domain") as string : null;
                            string host = k != null ? k.GetValue("Hostname") as string : null;
                            if (!Text.IsBlank(dns)) info.Fqdn = (host ?? info.ComputerName).ToLowerInvariant() + "." + dns;
                            else if (!Text.IsBlank(domain)) info.Fqdn = info.ComputerName + " @ " + domain;
                        }
                    }
                    NetApiBufferFree(name);
                }
            }
            catch (Exception)
            {
                // informational only
            }
            return info;
        }

        public List<PendingRebootReason> GetPendingRebootReasons()
        {
            var list = new List<PendingRebootReason>();
            const string cbs = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\";
            const string wu = @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\";

            Check(list, () => KeyExists(cbs + "RebootPending"), "Component Based Servicing: RebootPending", true);
            Check(list, () => KeyExists(cbs + "RebootInProgress"), "Component Based Servicing: RebootInProgress", true);
            Check(list, () => KeyExists(cbs + "PackagesPending"), "Component Based Servicing: PackagesPending", true);
            Check(list, () => KeyExists(wu + @"Auto Update\RebootRequired"), "Windows Update: Auto Update\\RebootRequired", true);
            Check(list, () => SubKeyCount(wu + @"Services\Pending") > 0, "Windows Update: Services\\Pending", true);
            Check(list, () =>
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Updates"))
                {
                    var v = k != null ? k.GetValue("UpdateExeVolatile") : null;
                    return v is int && (int)v != 0;
                }
            }, "Update.exe volatile flag (UpdateExeVolatile)", true);
            Check(list, () =>
            {
                string active = ReadString(@"SYSTEM\CurrentControlSet\Control\ComputerName\ActiveComputerName", "ComputerName");
                string pending = ReadString(@"SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName", "ComputerName");
                return active != null && pending != null && !Text.EqualsIgnoreCase(active, pending);
            }, "Computer rename pending", true);
            Check(list, () =>
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Netlogon"))
                {
                    if (k == null) return false;
                    foreach (var name in k.GetSubKeyNames())
                    {
                        if (Text.EqualsIgnoreCase(name, "JoinDomain") || Text.EqualsIgnoreCase(name, "AvoidSpnSet")) return true;
                    }
                    return false;
                }
            }, "Domain join pending", true);
            Check(list, () =>
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
                {
                    var v = k != null ? k.GetValue("PendingFileRenameOperations") as string[] : null;
                    if (v == null) return false;
                    foreach (var s in v)
                    {
                        if (!Text.IsBlank(s)) return true;
                    }
                    return false;
                }
            }, "Pending file rename operations (may be caused by non-update software)", false);
            return list;
        }

        private static void Check(List<PendingRebootReason> list, Func<bool> test, string source, bool definite)
        {
            try
            {
                if (test()) list.Add(new PendingRebootReason(source, definite));
            }
            catch (Exception)
            {
                // Registry access problems are not fatal for the reboot check.
            }
        }

        private static bool KeyExists(string path)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(path))
            {
                return k != null;
            }
        }

        private static int SubKeyCount(string path)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(path))
            {
                return k != null ? k.SubKeyCount : 0;
            }
        }

        private static string ReadString(string path, string name)
        {
            using (var k = Registry.LocalMachine.OpenSubKey(path))
            {
                return k != null ? k.GetValue(name) as string : null;
            }
        }
    }

    /// <summary>Reads/writes HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate (the values Group Policy uses).</summary>
    internal sealed class RegistryPolicyStore : IPolicyStore
    {
        private const string WuKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
        private const string AuKey = WuKey + @"\AU";

        public AutoUpdatePolicy Read()
        {
            var p = new AutoUpdatePolicy();
            using (var wu = Registry.LocalMachine.OpenSubKey(WuKey))
            {
                if (wu != null)
                {
                    p.WUServer = wu.GetValue("WUServer") as string;
                    p.WUStatusServer = wu.GetValue("WUStatusServer") as string;
                    p.TargetGroup = wu.GetValue("TargetGroup") as string;
                    p.TargetGroupEnabled = ReadInt(wu, "TargetGroupEnabled");
                    p.DoNotConnectToWindowsUpdateInternetLocations = ReadInt(wu, "DoNotConnectToWindowsUpdateInternetLocations");
                }
            }
            using (var au = Registry.LocalMachine.OpenSubKey(AuKey))
            {
                if (au != null)
                {
                    p.NoAutoUpdate = ReadInt(au, "NoAutoUpdate");
                    p.AUOptions = ReadInt(au, "AUOptions");
                    p.ScheduledInstallDay = ReadInt(au, "ScheduledInstallDay");
                    p.ScheduledInstallTime = ReadInt(au, "ScheduledInstallTime");
                    p.UseWUServer = ReadInt(au, "UseWUServer");
                }
            }
            return p;
        }

        public void Write(AutoUpdatePolicy p)
        {
            try
            {
                using (var wu = Registry.LocalMachine.CreateSubKey(WuKey))
                using (var au = Registry.LocalMachine.CreateSubKey(AuKey))
                {
                    SetString(wu, "WUServer", p.WUServer);
                    SetString(wu, "WUStatusServer", p.WUStatusServer);
                    SetString(wu, "TargetGroup", p.TargetGroup);
                    SetInt(wu, "TargetGroupEnabled", p.TargetGroupEnabled);
                    SetInt(wu, "DoNotConnectToWindowsUpdateInternetLocations", p.DoNotConnectToWindowsUpdateInternetLocations);
                    SetInt(au, "NoAutoUpdate", p.NoAutoUpdate);
                    SetInt(au, "AUOptions", p.AUOptions);
                    SetInt(au, "ScheduledInstallDay", p.ScheduledInstallDay);
                    SetInt(au, "ScheduledInstallTime", p.ScheduledInstallTime);
                    SetInt(au, "UseWUServer", p.UseWUServer);
                }
            }
            catch (UnauthorizedAccessException)
            {
                throw new NotElevatedException();
            }
            catch (System.Security.SecurityException)
            {
                throw new NotElevatedException();
            }
        }

        private static int? ReadInt(RegistryKey key, string name)
        {
            var v = key.GetValue(name);
            if (v is int) return (int)v;
            int n;
            if (v is string && int.TryParse((string)v, out n)) return n;
            return null;
        }

        private static void SetInt(RegistryKey key, string name, int? value)
        {
            if (value.HasValue) key.SetValue(name, value.Value, RegistryValueKind.DWord);
            else key.DeleteValue(name, false);
        }

        private static void SetString(RegistryKey key, string name, string value)
        {
            if (value != null) key.SetValue(name, value, RegistryValueKind.String);
            else key.DeleteValue(name, false);
        }
    }
}
