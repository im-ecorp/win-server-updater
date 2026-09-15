using System;
using WinUpdateManager.Cli;
using WinUpdateManager.Core;
using WinUpdateManager.Output;
using WinUpdateManager.Platform;
using WinUpdateManager.Simulation;

namespace WinUpdateManager
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            ParsedArgs parsed;
            try
            {
                parsed = CommandLineParser.Parse(args);
            }
            catch (UsageException ex)
            {
                ConsoleUi.Error(ex.Message);
                Console.Error.WriteLine("Run '" + App.ExeName + " help' for usage.");
                return ExitCodes.Usage;
            }

            ConsoleUi.NoColor = parsed.Flag("no-color");
            if (!parsed.Flag("no-log"))
                ConsoleUi.Log = new Logger(parsed.Get("log") ?? Logger.DefaultFilePath());

            bool simulate = parsed.Flag("simulate");
            bool infoOnly = parsed.Command == "help" || parsed.Command == "version" || parsed.Flag("help");
            if (!simulate && !infoOnly && Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                ConsoleUi.Error("WinUpdateManager manages Windows Update and must run on Windows Server. Use --simulate to try it here.");
                return ExitCodes.Error;
            }

            AppServices services;
            try
            {
                services = simulate || infoOnly ? SimulationState.CreateServices() : WindowsPlatform.CreateServices();
            }
            catch (Exception ex)
            {
                ConsoleUi.Error("Initialization failed: " + ex.Message);
                return ExitCodes.Error;
            }

            var app = new App(services);
            Console.CancelKeyPress += (sender, e) =>
            {
                if (app.CancelRequested) return; // second Ctrl+C terminates the process
                e.Cancel = true;
                app.RequestCancel();
                ConsoleUi.Warn("Ctrl+C: stopping after the current update. Press Ctrl+C again to exit immediately.");
            };

            try
            {
                return app.Run(parsed);
            }
            catch (Exception ex)
            {
                return app.HandleException(ex);
            }
        }
    }
}
