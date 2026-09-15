using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;
using Microsoft.Win32;
using WinUpdateManager.Core;

namespace WinUpdateManager.Platform
{
    internal sealed class WindowsServiceControl : IServiceControl
    {
        private static readonly string[] KnownServices =
        {
            "wuauserv", "bits", "cryptsvc", "msiserver", "trustedinstaller", "usosvc", "dosvc", "waasmedicsvc"
        };

        private const uint ScManagerConnect = 0x0001;
        private const uint ServiceChangeConfig = 0x0002;
        private const uint ServiceNoChange = 0xFFFFFFFF;

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenSCManager(string machineName, string databaseName, uint access);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint access);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool ChangeServiceConfig(IntPtr service, uint serviceType, uint startType, uint errorControl,
            string binaryPathName, string loadOrderGroup, IntPtr tagId, string dependencies, string serviceStartName,
            string password, string displayName);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool CloseServiceHandle(IntPtr handle);

        public IList<ServiceState> GetUpdateServices()
        {
            var list = new List<ServiceState>();
            foreach (var name in KnownServices)
            {
                var state = TryGet(name);
                if (state != null) list.Add(state);
            }
            return list;
        }

        public ServiceState Get(string name)
        {
            var state = TryGet(name);
            if (state == null) throw new InvalidOperationException("Service '" + name + "' was not found on this computer.");
            return state;
        }

        public void Start(string name)
        {
            using (var sc = Open(name))
            {
                if (sc.Status == ServiceControllerStatus.Running) return;
                if (ReadStartMode(sc.ServiceName) == "Disabled")
                    throw new InvalidOperationException("Service '" + name + "' is disabled. Set its startup type first: WinUpdateManager services startup " + name + " manual");
                if (sc.Status != ServiceControllerStatus.StartPending) sc.Start();
                Wait(sc, ServiceControllerStatus.Running, 90);
            }
        }

        public void Stop(string name)
        {
            using (var sc = Open(name))
            {
                if (sc.Status == ServiceControllerStatus.Stopped) return;
                if (sc.Status != ServiceControllerStatus.StopPending) sc.Stop();
                Wait(sc, ServiceControllerStatus.Stopped, 120);
            }
        }

        public void SetStartupMode(string name, StartupMode mode)
        {
            uint startType = mode == StartupMode.Automatic ? 2u : mode == StartupMode.Manual ? 3u : 4u;
            IntPtr scm = OpenSCManager(null, null, ScManagerConnect);
            if (scm == IntPtr.Zero) throw Win32("OpenSCManager");
            try
            {
                IntPtr svc = OpenService(scm, name, ServiceChangeConfig);
                if (svc == IntPtr.Zero) throw Win32("OpenService " + name);
                try
                {
                    if (!ChangeServiceConfig(svc, ServiceNoChange, startType, ServiceNoChange, null, null, IntPtr.Zero, null, null, null, null))
                        throw Win32("ChangeServiceConfig " + name);
                }
                finally
                {
                    CloseServiceHandle(svc);
                }
            }
            finally
            {
                CloseServiceHandle(scm);
            }
        }

        public void ResetUpdateComponents(Action<string> progress)
        {
            foreach (var name in new[] { "wuauserv", "usosvc", "dosvc", "bits", "cryptsvc" })
            {
                var state = TryGet(name);
                if (state == null || state.Status == "Stopped") continue;
                progress("Stopping " + name + "...");
                try
                {
                    Stop(name);
                }
                catch (Exception ex)
                {
                    progress("  warning: could not stop " + name + ": " + ex.Message);
                }
            }

            string windir = Environment.GetEnvironmentVariable("SystemRoot");
            if (Text.IsBlank(windir)) windir = Path.GetDirectoryName(Environment.SystemDirectory);
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            RenameFolder(Path.Combine(windir, "SoftwareDistribution"), stamp, progress);
            RenameFolder(Path.Combine(Environment.SystemDirectory, "catroot2"), stamp, progress);

            try
            {
                string downloader = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Microsoft\Network\Downloader");
                if (Directory.Exists(downloader))
                {
                    foreach (var file in Directory.GetFiles(downloader, "qmgr*.dat"))
                    {
                        File.Delete(file);
                        progress("Deleted BITS queue file " + Path.GetFileName(file));
                    }
                }
            }
            catch (Exception ex)
            {
                progress("  warning: could not clear the BITS queue: " + ex.Message);
            }

            foreach (var name in new[] { "cryptsvc", "bits", "wuauserv" })
            {
                var state = TryGet(name);
                if (state == null) continue;
                if (state.StartMode == "Disabled")
                {
                    progress("  warning: " + name + " is disabled and was not started.");
                    continue;
                }
                progress("Starting " + name + "...");
                try
                {
                    Start(name);
                }
                catch (Exception ex)
                {
                    progress("  warning: could not start " + name + ": " + ex.Message);
                }
            }
            progress("Old folders were kept as *.bak-" + stamp + "; delete them once updates work again.");
        }

        private static void RenameFolder(string path, string stamp, Action<string> progress)
        {
            if (!Directory.Exists(path))
            {
                progress(path + " does not exist, skipped.");
                return;
            }
            string target = path + ".bak-" + stamp;
            for (int attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    Directory.Move(path, target);
                    progress("Renamed " + path + " -> " + Path.GetFileName(target));
                    return;
                }
                catch (IOException ex)
                {
                    if (attempt == 5) progress("  warning: could not rename " + path + ": " + ex.Message);
                    else Thread.Sleep(2000);
                }
                catch (UnauthorizedAccessException ex)
                {
                    if (attempt == 5) progress("  warning: could not rename " + path + ": " + ex.Message);
                    else Thread.Sleep(2000);
                }
            }
        }

        private static ServiceController Open(string name)
        {
            var sc = new ServiceController(name);
            try
            {
                var unused = sc.Status;
                return sc;
            }
            catch (InvalidOperationException)
            {
                sc.Dispose();
                throw new InvalidOperationException("Service '" + name + "' was not found on this computer.");
            }
        }

        private static ServiceState TryGet(string name)
        {
            try
            {
                using (var sc = new ServiceController(name))
                {
                    return new ServiceState
                    {
                        Name = sc.ServiceName,
                        DisplayName = sc.DisplayName,
                        Status = sc.Status.ToString(),
                        StartMode = ReadStartMode(sc.ServiceName)
                    };
                }
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static string ReadStartMode(string name)
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + name))
                {
                    if (k == null) return "Unknown";
                    var start = k.GetValue("Start");
                    var delayed = k.GetValue("DelayedAutostart");
                    if (!(start is int)) return "Unknown";
                    switch ((int)start)
                    {
                        case 0: return "Boot";
                        case 1: return "System";
                        case 2: return delayed is int && (int)delayed == 1 ? "Automatic (Delayed)" : "Automatic";
                        case 3: return "Manual";
                        case 4: return "Disabled";
                        default: return "Unknown";
                    }
                }
            }
            catch (Exception)
            {
                return "Unknown";
            }
        }

        private static void Wait(ServiceController sc, ServiceControllerStatus status, int seconds)
        {
            try
            {
                sc.WaitForStatus(status, TimeSpan.FromSeconds(seconds));
            }
            catch (System.ServiceProcess.TimeoutException)
            {
                throw new InvalidOperationException("Timed out waiting for service '" + sc.ServiceName + "' to become " + status + ".");
            }
        }

        private static ApiException Win32(string operation)
        {
            int error = Marshal.GetLastWin32Error();
            if (error == 5) return new ApiException(operation + " failed: access denied.", unchecked((int)0x80070005));
            return new ApiException(operation + " failed: " + new Win32Exception(error).Message, unchecked((int)(0x80070000 | (uint)error)));
        }
    }
}
