using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using WinUpdateManager.Core;

namespace WinUpdateManager.Platform
{
    /// <summary>
    /// Windows Update Agent (WUA) API implementation: Microsoft.Update.Session and related COM classes.
    /// Available on every Windows Server version, including Server Core.
    /// </summary>
    internal sealed class WuaUpdateProvider : IUpdateProvider
    {
        private const string ClientId = "WinUpdateManager";
        private object session;

        private object Session
        {
            get
            {
                if (session == null)
                {
                    session = Com.Create("Microsoft.Update.Session");
                    Com.TrySet(session, "ClientApplicationID", ClientId);
                }
                return session;
            }
        }

        public IList<UpdateInfo> Search(SearchOptions options)
        {
            var searcher = Com.Call(Session, "CreateUpdateSearcher");
            Com.TrySet(searcher, "ClientApplicationID", ClientId);
            switch (options.Source)
            {
                case UpdateSource.Wsus:
                    Com.Set(searcher, "ServerSelection", 1);
                    break;
                case UpdateSource.WindowsUpdate:
                    Com.Set(searcher, "ServerSelection", 2);
                    break;
                case UpdateSource.MicrosoftUpdate:
                    Com.Set(searcher, "ServerSelection", 3);
                    Com.Set(searcher, "ServiceID", WellKnownServices.MicrosoftUpdate);
                    break;
            }

            var result = Com.Call(searcher, "Search", SearchCriteria.Build(options));
            var code = (OperationResult)Com.GetInt(result, "ResultCode");
            if (code != OperationResult.Succeeded && code != OperationResult.SucceededWithErrors)
                throw new ApiException("Windows Update search finished with result: " + code, 0);

            var list = new List<UpdateInfo>();
            foreach (var item in Com.Items(Com.Get(result, "Updates")))
            {
                list.Add(Map(item));
            }
            return list;
        }

        private static UpdateInfo Map(object item)
        {
            var u = new UpdateInfo { NativeHandle = item };
            var identity = Com.TryGet(item, "Identity");
            if (identity != null)
            {
                u.Id = Com.GetString(identity, "UpdateID");
                u.Revision = Com.GetInt(identity, "RevisionNumber");
            }
            u.Title = Com.GetString(item, "Title");
            u.Description = Com.GetString(item, "Description");
            u.Severity = Com.GetString(item, "MsrcSeverity");
            u.IsDownloaded = Com.GetBool(item, "IsDownloaded");
            u.IsHidden = Com.GetBool(item, "IsHidden");
            u.IsInstalled = Com.GetBool(item, "IsInstalled");
            u.IsMandatory = Com.GetBool(item, "IsMandatory");
            u.IsOptional = Com.GetBool(item, "BrowseOnly");
            u.EulaAccepted = Com.GetBool(item, "EulaAccepted");
            u.IsUninstallable = Com.GetBool(item, "IsUninstallable");
            u.RebootRequired = Com.GetBool(item, "RebootRequired");
            u.MaxDownloadSize = Com.GetDecimal(item, "MaxDownloadSize");
            u.ReleaseDate = Com.GetDate(item, "LastDeploymentChangeTime");
            u.SupportUrl = Com.GetString(item, "SupportUrl");
            u.Kind = Com.GetInt(item, "Type") == 2 ? UpdateKind.Driver : UpdateKind.Software;

            foreach (var kb in Com.Strings(Com.TryGet(item, "KBArticleIDs")))
            {
                u.KbArticleIds.Add(Text.NormalizeKb(kb));
            }
            if (u.KbArticleIds.Count == 0)
            {
                var kb = Text.ExtractKb(u.Title);
                if (kb != null) u.KbArticleIds.Add(kb);
            }

            foreach (var category in Com.Items(Com.TryGet(item, "Categories")))
            {
                string type = Com.GetString(category, "Type");
                string name = Com.GetString(category, "Name");
                if (name == null) continue;
                if (Text.EqualsIgnoreCase(type, "UpdateClassification")) u.Classifications.Add(name);
                else if (Text.EqualsIgnoreCase(type, "Product")) u.Products.Add(name);
            }

            var behavior = Com.TryGet(item, "InstallationBehavior");
            if (behavior != null)
            {
                int rb = Com.GetInt(behavior, "RebootBehavior");
                u.RebootBehavior = rb >= 0 && rb <= 2 ? (RebootBehavior)rb : RebootBehavior.CanRequestReboot;
            }
            return u;
        }

        public void AcceptEula(UpdateInfo update)
        {
            Com.Call(Handle(update), "AcceptEula");
            update.EulaAccepted = true;
        }

        public UpdateActionResult Download(UpdateInfo update)
        {
            var downloader = Com.Call(Session, "CreateUpdateDownloader");
            Com.TrySet(downloader, "ClientApplicationID", ClientId);
            Com.Set(downloader, "Updates", SingleCollection(update));
            Com.TrySet(downloader, "Priority", 3); // dpHigh
            var result = ReadResult(Com.Call(downloader, "Download"), update, "download");
            update.IsDownloaded = result.IsSuccess || Com.GetBool(update.NativeHandle, "IsDownloaded");
            return result;
        }

        public UpdateActionResult Install(UpdateInfo update)
        {
            var installer = CreateInstaller(update);
            var result = ReadResult(Com.Call(installer, "Install"), update, "install");
            if (result.IsSuccess)
            {
                update.IsInstalled = true;
                update.RebootRequired = result.RebootRequired;
            }
            return result;
        }

        public UpdateActionResult Uninstall(UpdateInfo update)
        {
            var installer = CreateInstaller(update);
            var result = ReadResult(Com.Call(installer, "Uninstall"), update, "uninstall");
            if (result.IsSuccess) update.IsInstalled = false;
            return result;
        }

        private object CreateInstaller(UpdateInfo update)
        {
            var installer = Com.Call(Session, "CreateUpdateInstaller");
            Com.TrySet(installer, "ClientApplicationID", ClientId);
            Com.Set(installer, "Updates", SingleCollection(update));
            Com.TrySet(installer, "AllowSourcePrompts", false);
            Com.TrySet(installer, "ForceQuiet", true);
            return installer;
        }

        public void SetHidden(UpdateInfo update, bool hidden)
        {
            Com.Set(Handle(update), "IsHidden", hidden);
            update.IsHidden = hidden;
        }

        public IList<HistoryEntry> GetHistory(int count)
        {
            var list = new List<HistoryEntry>();
            var searcher = Com.Call(Session, "CreateUpdateSearcher");
            int total = Com.ToInt(Com.Call(searcher, "GetTotalHistoryCount"));
            if (total <= 0) return list;

            var entries = Com.Call(searcher, "QueryHistory", 0, Math.Min(total, count));
            foreach (var e in Com.Items(entries))
            {
                var date = Com.GetDate(e, "Date");
                int op = Com.GetInt(e, "Operation");
                var h = new HistoryEntry
                {
                    Date = date.HasValue ? DateTime.SpecifyKind(date.Value, DateTimeKind.Utc).ToLocalTime() : (DateTime?)null,
                    Operation = op == 1 ? "Installation" : op == 2 ? "Uninstallation" : "Other",
                    Result = (OperationResult)Com.GetInt(e, "ResultCode"),
                    HResult = Com.GetInt(e, "HResult"),
                    Title = Com.GetString(e, "Title"),
                    ClientApplication = Com.GetString(e, "ClientApplicationID")
                };
                var identity = Com.TryGet(e, "UpdateIdentity");
                if (identity != null) h.UpdateId = Com.GetString(identity, "UpdateID");
                h.Kb = Text.ExtractKb(h.Title);
                list.Add(h);
            }
            return list;
        }

        public AgentInfo GetAgentInfo()
        {
            var info = new AgentInfo();
            try
            {
                var agent = Com.Create("Microsoft.Update.AgentInfo");
                info.Version = Convert.ToString(Com.Call(agent, "GetInfo", "ProductVersionString"));
            }
            catch (ApiException)
            {
                try
                {
                    string dll = Path.Combine(Environment.SystemDirectory, "wuaueng.dll");
                    if (File.Exists(dll)) info.Version = FileVersionInfo.GetVersionInfo(dll).ProductVersion;
                }
                catch (Exception)
                {
                    // ignore
                }
            }

            try
            {
                var au = Com.Create("Microsoft.Update.AutoUpdate");
                var enabled = Com.TryGet(au, "ServiceEnabled");
                if (enabled is bool) info.AutomaticUpdatesEnabled = (bool)enabled;
                var results = Com.TryGet(au, "Results");
                if (results != null)
                {
                    info.LastSearchSuccess = ToLocal(Com.GetDate(results, "LastSearchSuccessDate"));
                    info.LastInstallSuccess = ToLocal(Com.GetDate(results, "LastInstallationSuccessDate"));
                }
            }
            catch (ApiException)
            {
                // Microsoft.Update.AutoUpdate is optional information
            }

            try
            {
                var systemInfo = Com.Create("Microsoft.Update.SystemInfo");
                info.RebootRequired = Com.GetBool(systemInfo, "RebootRequired");
            }
            catch (ApiException)
            {
                // ignore
            }
            return info;
        }

        public bool IsInstallerBusy()
        {
            return Com.GetBool(Com.Call(Session, "CreateUpdateInstaller"), "IsBusy");
        }

        public bool IsRebootRequiredBeforeInstallation()
        {
            return Com.GetBool(Com.Call(Session, "CreateUpdateInstaller"), "RebootRequiredBeforeInstallation");
        }

        public IList<UpdateServiceInfo> GetUpdateServices()
        {
            var list = new List<UpdateServiceInfo>();
            var manager = Com.Create("Microsoft.Update.ServiceManager");
            foreach (var svc in Com.Items(Com.Get(manager, "Services")))
            {
                list.Add(new UpdateServiceInfo
                {
                    Name = Com.GetString(svc, "Name"),
                    ServiceId = Com.GetString(svc, "ServiceID"),
                    IsDefaultAUService = Com.GetBool(svc, "IsDefaultAUService"),
                    IsManaged = Com.GetBool(svc, "IsManaged"),
                    IsRegisteredWithAU = Com.GetBool(svc, "IsRegisteredWithAU")
                });
            }
            return list;
        }

        public void RegisterMicrosoftUpdate()
        {
            var manager = Com.Create("Microsoft.Update.ServiceManager");
            Com.TrySet(manager, "ClientApplicationID", ClientId);
            // 7 = asfAllowPendingRegistration | asfAllowOnlineRegistration | asfRegisterServiceWithAU
            Com.Call(manager, "AddService2", WellKnownServices.MicrosoftUpdate, 7, string.Empty);
        }

        public void UnregisterMicrosoftUpdate()
        {
            var manager = Com.Create("Microsoft.Update.ServiceManager");
            Com.TrySet(manager, "ClientApplicationID", ClientId);
            Com.Call(manager, "RemoveService", WellKnownServices.MicrosoftUpdate);
        }

        private static object Handle(UpdateInfo update)
        {
            if (update.NativeHandle == null)
                throw new ApiException("The update object is no longer valid. Search again.", 0);
            return update.NativeHandle;
        }

        private static object SingleCollection(UpdateInfo update)
        {
            var collection = Com.Create("Microsoft.Update.UpdateColl");
            Com.Call(collection, "Add", Handle(update));
            return collection;
        }

        private static UpdateActionResult ReadResult(object result, UpdateInfo update, string action)
        {
            var r = new UpdateActionResult
            {
                Update = update,
                Action = action,
                Result = (OperationResult)Com.GetInt(result, "ResultCode"),
                HResult = Com.GetInt(result, "HResult"),
                RebootRequired = Com.GetBool(result, "RebootRequired")
            };
            try
            {
                var single = Com.Call(result, "GetUpdateResult", 0);
                var code = (OperationResult)Com.GetInt(single, "ResultCode");
                int hr = Com.GetInt(single, "HResult");
                if (code != OperationResult.NotStarted) r.Result = code;
                if (hr != 0) r.HResult = hr;
                if (Com.GetBool(single, "RebootRequired")) r.RebootRequired = true;
            }
            catch (ApiException)
            {
                // per-update result not available
            }
            return r;
        }

        private static DateTime? ToLocal(DateTime? utc)
        {
            return utc.HasValue ? DateTime.SpecifyKind(utc.Value, DateTimeKind.Utc).ToLocalTime() : (DateTime?)null;
        }
    }
}
