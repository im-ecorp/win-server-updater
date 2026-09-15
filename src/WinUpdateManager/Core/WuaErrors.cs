using System.Collections.Generic;
using System.ComponentModel;

namespace WinUpdateManager.Core
{
    /// <summary>Human readable descriptions for the most common Windows Update error codes.</summary>
    public static class WuaErrors
    {
        private static readonly Dictionary<uint, string> Known = new Dictionary<uint, string>
        {
            { 0x80240001, "WU_E_NO_SERVICE: Windows Update Agent was unable to provide the service." },
            { 0x8024000B, "WU_E_CALL_CANCELLED: The operation was cancelled." },
            { 0x8024000C, "WU_E_NOOP: No operation was required." },
            { 0x80240016, "WU_E_INSTALL_NOT_ALLOWED: Another installation is in progress or the system is waiting for a reboot." },
            { 0x80240017, "WU_E_NOT_APPLICABLE: The update is not applicable to this computer." },
            { 0x8024001E, "WU_E_SERVICE_STOP: The service or system was shutting down." },
            { 0x80240020, "WU_E_NO_INTERACTIVE_USER: The operation requires an interactive user." },
            { 0x80240022, "WU_E_ALL_UPDATES_FAILED: The operation failed for all updates." },
            { 0x80240024, "WU_E_NO_UPDATE: There are no updates." },
            { 0x80240025, "WU_E_USER_ACCESS_DISABLED: Group Policy does not allow this user to use Windows Update." },
            { 0x8024002E, "WU_E_WU_DISABLED: Access to an unmanaged server is not allowed (Windows Update is blocked by policy; use --source wsus or check GPO)." },
            { 0x80240032, "WU_E_INVALID_CRITERIA: The search criteria string is invalid." },
            { 0x80240044, "WU_E_PER_MACHINE_UPDATE_ACCESS_DENIED: Only administrators can do this for per-machine updates." },
            { 0x80240438, "WU_E_PT_ENDPOINT_UNREACHABLE: No network route to the update server." },
            { 0x80244007, "WU_E_PT_SOAPCLIENT_SOAPFAULT: The server returned a SOAP fault (check WSUS health and the server clock)." },
            { 0x80244010, "WU_E_PT_EXCEEDED_MAX_SERVER_TRIPS: Too many round trips to the server." },
            { 0x80244017, "WU_E_PT_HTTP_STATUS_DENIED: HTTP 401 - access denied by the update server or proxy." },
            { 0x80244018, "WU_E_PT_HTTP_STATUS_FORBIDDEN: HTTP 403 - forbidden (often a proxy is blocking access)." },
            { 0x80244019, "WU_E_PT_HTTP_STATUS_NOT_FOUND: HTTP 404 - the update server URL is wrong (check the WSUS address/port)." },
            { 0x8024401C, "WU_E_PT_HTTP_STATUS_REQUEST_TIMEOUT: The update server timed out." },
            { 0x8024401F, "WU_E_PT_HTTP_STATUS_SERVER_ERROR: HTTP 500 - the update server had an internal error." },
            { 0x80244022, "WU_E_PT_HTTP_STATUS_SERVICE_UNAVAIL: HTTP 503 - the update server is unavailable (WSUS app pool stopped?)." },
            { 0x8024402C, "WU_E_PT_WINHTTP_NAME_NOT_RESOLVED: The update server or proxy name cannot be resolved (DNS)." },
            { 0x80246007, "WU_E_DM_NOTDOWNLOADED: The update has not been downloaded." },
            { 0x80248007, "WU_E_DS_NODATA: The requested information is not in the Windows Update data store." },
            { 0x80248014, "WU_E_DS_UNKNOWNSERVICE: The update service is not registered (run 'microsoft-update enable' first)." },
            { 0x8024A000, "WU_E_AU_NOSERVICE: Automatic Updates could not service the request." },
            { 0x80070005, "E_ACCESSDENIED: Access is denied. Run the tool elevated (as Administrator or SYSTEM)." },
            { 0x8007000E, "E_OUTOFMEMORY: Not enough memory." },
            { 0x80070070, "ERROR_DISK_FULL: There is not enough free disk space." },
            { 0x80070422, "ERROR_SERVICE_DISABLED: The Windows Update service (wuauserv) is disabled. Run 'services startup wuauserv manual'." },
            { 0x80070643, "ERROR_INSTALL_FAILURE: Fatal error during installation." },
            { 0x800706BA, "RPC_S_SERVER_UNAVAILABLE: The RPC server is unavailable." },
            { 0x80070BC9, "ERROR_FAIL_REBOOT_REQUIRED: A reboot is required from a previous installation." },
            { 0x80072EE2, "ERROR_WINHTTP_TIMEOUT: The connection to the update server timed out." },
            { 0x80072EE7, "ERROR_WINHTTP_NAME_NOT_RESOLVED: The server name could not be resolved (DNS/proxy)." },
            { 0x80072EFD, "ERROR_WINHTTP_CANNOT_CONNECT: Cannot connect to the update server (firewall, proxy or network)." },
            { 0x80072EFE, "ERROR_WINHTTP_CONNECTION_ERROR: The connection to the update server was terminated." },
            { 0x80072F8F, "ERROR_WINHTTP_SECURE_FAILURE: TLS/certificate error - check the system clock, TLS 1.2 and root certificates." },
            { 0x80073712, "ERROR_SXS_COMPONENT_STORE_CORRUPT: The component store is corrupt. Run: DISM /Online /Cleanup-Image /RestoreHealth" },
            { 0x800F081F, "CBS_E_SOURCE_MISSING: The source files could not be found (DISM /RestoreHealth may help)." },
            { 0x800F0922, "CBS_E_INSTALLERS_FAILED: Servicing failed (low space on the System Reserved partition or a network problem)." },
        };

        public static string Describe(int errorCode)
        {
            if (errorCode == 0) return "No error code.";

            string description;
            if (Known.TryGetValue(unchecked((uint)errorCode), out description)) return description;

            // FACILITY_WIN32 HRESULTs (0x8007xxxx) map to Win32 error messages.
            uint code = unchecked((uint)errorCode);
            if ((code & 0xFFFF0000) == 0x80070000)
            {
                try
                {
                    return new Win32Exception((int)(code & 0xFFFF)).Message;
                }
                catch
                {
                    // ignore - fall through to generic text
                }
            }
            if ((code & 0xFFFF0000) == 0x80240000)
                return "Windows Update Agent error. Search the code on learn.microsoft.com ('Windows Update error codes').";

            return "Unknown error.";
        }

        public static string Format(int errorCode)
        {
            return Text.Hex(errorCode) + " - " + Describe(errorCode);
        }
    }
}
