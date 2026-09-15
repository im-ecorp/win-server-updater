namespace WinUpdateManager.Cli
{
    public static class HelpText
    {
        public const string Filters =
@"  Filter options (list, download, install, hide, unhide, schedule):
    --kb <list>             Only these KB numbers, e.g. --kb KB5030216,5031364
    --exclude-kb <list>     Skip these KB numbers
    --category <list>       Classification contains, e.g. ""Security,Critical,Definition""
    --severity <list>       MSRC severity: Critical, Important, Moderate, Low, Unspecified
    --title <text>          Title contains (comma separated for several)
    --exclude-title <text>  Skip updates whose title contains the text, e.g. Preview
    --id <guid>             Windows Update update ID
    --max-size-mb <n>       Skip updates larger than n MB
    --include-optional      Include optional updates (preview CUs, feature on demand)
    --include-drivers       Include driver updates
    --source <name>         default | wsus | windowsupdate | microsoftupdate
    --criteria <text>       Raw WUA search criteria, e.g. ""IsInstalled=0 and Type='Software'""";

        public const string General =
@"WinUpdateManager - Windows Update manager for Windows Server 2008 / 2008 R2 / 2012 / 2012 R2 /
2016 / 2019 / 2022 / 2025 (Desktop Experience and Server Core).

Usage:
  WinUpdateManager                          Start the interactive menu
  WinUpdateManager <command> [options]

Commands:
  status                    System, Windows Update configuration and reboot state
  list                      Search available updates (--hidden, --installed for other scopes)
  download                  Download matching updates without installing
  install                   Download and install matching updates
  uninstall --kb <KB>       Uninstall an installed update
  hide / unhide             Hide or unhide updates so they are not installed
  history [--count n]       Windows Update installation history
  pending-reboot            Check whether a restart is pending (exit code 3010 if so)
  reboot [--delay s]        Restart the server (--abort to cancel a pending restart)
  config [show|set|reset]   Automatic Updates policy and WSUS server settings
  services [status|start|stop|restart|startup|reset]
                            Manage Windows Update services and repair components
  schedule [show|create|delete]
                            Scheduled automatic installation (Task Scheduler, runs as SYSTEM)
  microsoft-update [status|enable|disable]
                            Receive updates for other Microsoft products (SQL Server, .NET ...)
  menu                      Interactive menu
  help [command]            Help for a command
  version                   Show version

Global options:
  --json                    Machine readable JSON output
  --yes, -y                 Do not ask for confirmation (required for unattended runs)
  --simulate                Use a built-in simulated server; nothing on this machine is changed
  --log <file>              Log file (default %ProgramData%\WinUpdateManager\logs)
  --no-log                  Do not write a log file
  --quiet, -q               Only print warnings and errors
  --no-color                Disable colored output

Exit codes:
  0 success | 1 error | 2 invalid usage | 4 some updates failed | 5 administrator rights required
  3010 success, restart required

Examples:
  WinUpdateManager status
  WinUpdateManager list --category Security,Critical
  WinUpdateManager install --yes --reboot if-required --exclude-title Preview
  WinUpdateManager install --kb KB5030216 --yes
  WinUpdateManager config set --wsus-server http://wsus01:8530 --mode download --restart-service
  WinUpdateManager schedule create --frequency weekly --day sunday --at 03:00 --reboot if-required
  WinUpdateManager services reset --yes
  WinUpdateManager --simulate            (try the tool safely)

Run 'WinUpdateManager help <command>' for details.";

        public static string For(string command)
        {
            switch (command)
            {
                case "list":
                    return
@"WinUpdateManager list [filters] [--hidden | --installed] [--json]

Searches Windows Update / WSUS for updates that are not installed yet. Optional updates and drivers
are skipped unless --include-optional / --include-drivers are given.

  --hidden      Show hidden updates instead
  --installed   Show installed updates instead

" + Filters;

                case "download":
                    return
@"WinUpdateManager download [filters] [--dry-run] [--yes]

Downloads matching updates to the local cache without installing them.

  --dry-run          Show what would be downloaded
  --no-accept-eula   Skip updates that require accepting a license agreement

" + Filters;

                case "install":
                    return
@"WinUpdateManager install [filters] [--yes] [--reboot never|if-required|always] [--reboot-delay s] [--dry-run]

Downloads and installs matching updates one by one, showing the result of each update.
Press Ctrl+C once to stop after the current update.

  --reboot <mode>      never (default), if-required, always
  --reboot-delay <s>   Seconds before the restart (default 120)
  --dry-run            Show what would be installed
  --force              Install even when Windows reports a restart is required first
  --no-accept-eula     Skip updates that require accepting a license agreement

Exit code 3010 means updates were installed and a restart is still required.

" + Filters;

                case "uninstall":
                    return
@"WinUpdateManager uninstall --kb <KB> [--yes] [--reboot never|if-required|always] [--dry-run]

Uninstalls an installed update through the Windows Update Agent. Some updates (e.g. cumulative
updates on newer servers) cannot be removed this way; the tool then prints the DISM/wusa alternative.";

                case "hide":
                case "unhide":
                    return
@"WinUpdateManager hide|unhide [filters] [--all] [--yes] [--dry-run]

Hidden updates are ignored by searches and automatic installation. A filter (for example --kb) is
required unless --all is given.

" + Filters;

                case "history":
                    return
@"WinUpdateManager history [--count n] [--kb <list>] [--title <text>] [--json]

Shows the Windows Update installation history (default: last 30 entries).";

                case "pending-reboot":
                    return
@"WinUpdateManager pending-reboot [--json]

Checks Component Based Servicing, Windows Update, pending file renames, computer rename and domain
join indicators. Exit code 3010 when a restart is pending, otherwise 0.";

                case "reboot":
                    return
@"WinUpdateManager reboot [--delay seconds] [--yes]
WinUpdateManager reboot --abort

Schedules a restart of this server (default delay 60 seconds) or cancels a scheduled restart.";

                case "config":
                    return
@"WinUpdateManager config show [--json]
WinUpdateManager config set [options] [--restart-service]
WinUpdateManager config reset [--yes]

Configures the Windows Update policy registry values (same values Group Policy writes).
Domain Group Policy may override local values on its next refresh.

  --mode <mode>                 disabled | notify | download | scheduled | local-admin
  --day <day>                   every | sunday..saturday (scheduled installs)
  --time <HH>                   Hour 0-23 for scheduled installs
  --wsus-server <url>           Use a WSUS server, e.g. http://wsus01:8530
  --wsus-status-server <url>    WSUS reporting server (defaults to --wsus-server)
  --target-group <name>         WSUS client-side targeting group ("" to clear)
  --clear-wsus                  Stop using WSUS (go back to Windows Update)
  --restart-service             Restart wuauserv so changes apply immediately";

                case "services":
                    return
@"WinUpdateManager services [status] [--json]
WinUpdateManager services start|stop|restart [service]      (default service: wuauserv)
WinUpdateManager services startup <service> automatic|manual|disabled
WinUpdateManager services reset [--yes]

'reset' repairs Windows Update: stops the update services, renames SoftwareDistribution and
catroot2 (kept as backups), clears the BITS queue and starts the services again.";

                case "schedule":
                    return
@"WinUpdateManager schedule show [--name <task>] [--json]
WinUpdateManager schedule create [--frequency daily|weekly] [--day sunday] [--at 03:00]
                                 [--reboot if-required] [--reboot-delay s] [filters]
WinUpdateManager schedule delete [--name <task>]

Creates a Windows Task Scheduler task that runs 'install --yes' as SYSTEM with the given filters.
Keep the executable in a permanent folder (e.g. C:\Tools\WinUpdateManager) before creating the task.

" + Filters;

                case "microsoft-update":
                    return
@"WinUpdateManager microsoft-update [status|enable|disable] [--json]

Registers the Microsoft Update service so that updates for other Microsoft products installed on
the server (SQL Server, Exchange, .NET, Visual C++ runtimes...) are offered too.";

                case "status":
                    return
@"WinUpdateManager status [--json]

Shows the operating system, support lifecycle, update source (WSUS or internet), automatic update
policy, Windows Update Agent version, last scan/install times, service states and reboot state.";

                default:
                    return General;
            }
        }
    }
}
