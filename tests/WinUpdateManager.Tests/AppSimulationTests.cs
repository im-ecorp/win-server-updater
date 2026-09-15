using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using WinUpdateManager.Cli;
using WinUpdateManager.Output;
using WinUpdateManager.Simulation;
using Xunit;

namespace WinUpdateManager.Tests
{
    [CollectionDefinition("Console", DisableParallelization = true)]
    public class ConsoleCollection
    {
    }

    /// <summary>End-to-end command tests against the simulated server.</summary>
    [Collection("Console")]
    public sealed class AppSimulationTests : IDisposable
    {
        private readonly TextWriter originalOut = Console.Out;
        private readonly TextWriter originalError = Console.Error;
        private readonly TextReader originalIn = Console.In;
        private readonly StringWriter output = new StringWriter();
        private readonly StringWriter error = new StringWriter();
        private readonly App app;

        public AppSimulationTests()
        {
            SimulationState.DelayMs = 0;
            Console.SetOut(output);
            Console.SetError(error);
            Console.SetIn(new StringReader(string.Empty));
            ConsoleUi.NoColor = true;
            ConsoleUi.Quiet = false;
            ConsoleUi.Log = Logger.Null;
            app = new App(SimulationState.CreateServices());
        }

        public void Dispose()
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            Console.SetIn(originalIn);
            ConsoleUi.Quiet = false;
        }

        private int Run(params string[] args)
        {
            output.GetStringBuilder().Clear();
            error.GetStringBuilder().Clear();
            return app.Run(CommandLineParser.Parse(args));
        }

        private JsonElement RunJson(params string[] args)
        {
            int code = Run(args.Concat(new[] { "--json" }).ToArray());
            Assert.True(code == 0, "exit code " + code + ": " + error);
            using (var doc = JsonDocument.Parse(output.ToString()))
            {
                return doc.RootElement.Clone();
            }
        }

        private int AvailableCount(params string[] extra)
        {
            return RunJson(new[] { "list" }.Concat(extra).ToArray()).GetProperty("count").GetInt32();
        }

        [Fact]
        public void Help_And_Version()
        {
            Assert.Equal(0, Run("help"));
            Assert.Contains("Commands:", output.ToString());
            Assert.Equal(0, Run("help", "install"));
            Assert.Contains("--reboot", output.ToString());
            Assert.Equal(0, Run("version"));
            Assert.Contains(App.Version, output.ToString());
        }

        [Fact]
        public void Status_Text_And_Json()
        {
            Assert.Equal(0, Run("status"));
            string text = output.ToString();
            Assert.Contains("Windows Server 2022", text);
            Assert.Contains("Update source", text);

            var json = RunJson("status");
            Assert.Equal("SIM-SRV01", json.GetProperty("system").GetProperty("computerName").GetString());
            Assert.False(json.GetProperty("reboot").GetProperty("pending").GetBoolean());
        }

        [Fact]
        public void List_DefaultExcludesOptionalDriversAndMicrosoftUpdateProducts()
        {
            Assert.Equal(5, AvailableCount());
            Assert.Equal(7, AvailableCount("--include-optional", "--include-drivers"));
            Assert.Equal(2, AvailableCount("--category", "Security"));
            Assert.Equal(1, AvailableCount("--kb", "KB5066789")); // explicit KB includes optional
        }

        [Fact]
        public void List_Text_ShowsTable()
        {
            Assert.Equal(0, Run("list"));
            Assert.Contains("KB5065432", output.ToString());
            Assert.Contains("Classification", output.ToString());
        }

        [Fact]
        public void MicrosoftUpdate_Enable_OffersOtherProducts()
        {
            Assert.False(RunJson("microsoft-update").GetProperty("microsoftUpdateRegistered").GetBoolean());
            Assert.Equal(0, Run("microsoft-update", "enable"));
            Assert.True(RunJson("microsoft-update", "status").GetProperty("microsoftUpdateRegistered").GetBoolean());
            Assert.Equal(6, AvailableCount());
            Assert.Equal(0, Run("mu", "disable"));
            Assert.Equal(5, AvailableCount());
        }

        [Fact]
        public void Install_DryRun_ChangesNothing()
        {
            Assert.Equal(0, Run("install", "--dry-run"));
            Assert.Contains("Dry run", output.ToString());
            Assert.Equal(5, AvailableCount());
        }

        [Fact]
        public void Install_WithoutYesAndNoInput_Fails()
        {
            Assert.Equal(ExitCodes.Error, Run("install"));
            Assert.Contains("--yes", error.ToString());
            Assert.Equal(5, AvailableCount());
        }

        [Fact]
        public void Install_All_ThenRetryFailedUpdate()
        {
            Assert.Equal(ExitCodes.PartialFailure, Run("install", "--yes"));
            Assert.Contains("Installation summary", output.ToString());
            Assert.Equal(1, AvailableCount());

            Assert.Equal(ExitCodes.RebootRequired, Run("install", "--kb", "KB5062222", "--yes"));
            Assert.Equal(0, AvailableCount());
            Assert.Equal(ExitCodes.RebootRequired, Run("pending-reboot"));

            // Nothing left to install but a reboot is still pending.
            Assert.Equal(ExitCodes.RebootRequired, Run("install", "--yes"));
        }

        [Fact]
        public void Install_Json_ReportsResults()
        {
            int code = Run("install", "--kb", "KB2267602", "--yes", "--json");
            Assert.Equal(0, code);
            using (var doc = JsonDocument.Parse(output.ToString()))
            {
                var root = doc.RootElement;
                Assert.Equal(1, root.GetProperty("succeeded").GetInt32());
                Assert.False(root.GetProperty("rebootRequired").GetBoolean());
                Assert.Equal("Succeeded", root.GetProperty("results")[0].GetProperty("result").GetString());
            }
        }

        [Fact]
        public void Install_RebootIfRequired_SchedulesRestart()
        {
            Assert.Equal(0, Run("install", "--kb", "KB5065432", "--yes", "--reboot", "if-required", "--reboot-delay", "30"));
            Assert.Contains("[simulation] A real server would now restart in 30 seconds", output.ToString());
            Assert.Equal(0, Run("reboot", "--abort"));
            Assert.Equal(ExitCodes.Error, Run("reboot", "--abort"));
        }

        [Fact]
        public void Download_Only()
        {
            Assert.Equal(0, Run("download", "--category", "Definition", "--yes"));
            var list = RunJson("list", "--kb", "KB2267602");
            Assert.True(list.GetProperty("updates")[0].GetProperty("isDownloaded").GetBoolean());
            Assert.False(list.GetProperty("updates")[0].GetProperty("isInstalled").GetBoolean());
        }

        [Fact]
        public void Hide_And_Unhide()
        {
            Assert.Equal(ExitCodes.Usage, Run("hide", "--yes"));
            Assert.Equal(0, Run("hide", "--kb", "KB890830", "--yes"));
            Assert.Equal(4, AvailableCount());
            var hidden = RunJson("list", "--hidden");
            Assert.Contains(hidden.GetProperty("updates").EnumerateArray(), u => u.GetProperty("kb")[0].GetString() == "KB890830");

            Assert.Equal(0, Run("unhide", "--kb", "890830", "--yes"));
            Assert.Equal(5, AvailableCount());
        }

        [Fact]
        public void Uninstall()
        {
            Assert.Equal(ExitCodes.Usage, Run("uninstall"));
            Assert.Equal(ExitCodes.Error, Run("uninstall", "--kb", "KB5063880", "--yes"));
            Assert.Contains("wusa.exe", error.ToString());
            Assert.Equal(ExitCodes.RebootRequired, Run("uninstall", "--kb", "KB5062001", "--yes"));
        }

        [Fact]
        public void History()
        {
            var json = RunJson("history", "--count", "10");
            Assert.True(json.GetProperty("count").GetInt32() >= 4);
            var filtered = RunJson("history", "--kb", "KB5062222");
            Assert.Equal(1, filtered.GetProperty("count").GetInt32());
            Assert.Equal("Failed", filtered.GetProperty("history")[0].GetProperty("result").GetString());
        }

        [Fact]
        public void Config_ScheduleAndWsus()
        {
            Assert.Equal(0, Run("config", "set", "--mode", "scheduled", "--day", "sunday", "--time", "4"));
            var policy = RunJson("config", "show");
            Assert.Equal(4, policy.GetProperty("AUOptions").GetInt32());
            Assert.Equal(1, policy.GetProperty("ScheduledInstallDay").GetInt32());
            Assert.Equal(4, policy.GetProperty("ScheduledInstallTime").GetInt32());

            Assert.Equal(0, Run("config", "set", "--wsus-server", "http://wsus01:8530/", "--target-group", "Servers", "--restart-service"));
            Assert.Contains("wuauserv restarted", output.ToString());
            var status = RunJson("status");
            Assert.Equal("WSUS (http://wsus01:8530)", status.GetProperty("windowsUpdate").GetProperty("effectiveSource").GetString());

            Assert.Equal(0, Run("config", "set", "--clear-wsus"));
            Assert.Equal(JsonValueKind.Null, RunJson("config").GetProperty("WUServer").ValueKind);

            Assert.Equal(ExitCodes.Usage, Run("config", "set", "--wsus-server", "wsus01"));
            Assert.Equal(ExitCodes.Usage, Run("config", "set"));
            Assert.Equal(0, Run("config", "reset", "--yes"));
            Assert.False(RunJson("config").GetProperty("configured").GetBoolean());
        }

        [Fact]
        public void Search_FromUnconfiguredWsus_ReportsError()
        {
            Assert.Equal(ExitCodes.Error, Run("list", "--source", "wsus"));
            Assert.Contains("0x8024002B", error.ToString());
        }

        [Fact]
        public void Services()
        {
            var json = RunJson("services");
            Assert.True(json.GetProperty("services").GetArrayLength() >= 3);
            Assert.Equal(0, Run("services", "restart", "bits"));
            Assert.Equal(0, Run("services", "startup", "wuauserv", "disabled"));
            Assert.Equal(ExitCodes.Error, Run("services", "start", "wuauserv"));
            Assert.Equal(0, Run("services", "startup", "wuauserv", "manual"));
            Assert.Equal(0, Run("services", "reset", "--yes"));
            Assert.Contains("SoftwareDistribution", output.ToString());
            Assert.Equal(ExitCodes.Error, Run("services", "start", "doesnotexist"));
        }

        [Fact]
        public void Schedule_CreateShowDelete()
        {
            Assert.False(RunJson("schedule").GetProperty("exists").GetBoolean());
            Assert.Equal(0, Run("schedule", "create", "--frequency", "weekly", "--day", "monday", "--at", "02:30", "--category", "Security", "--exclude-title", "Preview"));
            var task = RunJson("schedule", "show");
            Assert.True(task.GetProperty("exists").GetBoolean());
            Assert.Equal("Every Monday at 02:30", task.GetProperty("schedule").GetString());
            string arguments = task.GetProperty("arguments").GetString();
            Assert.Contains("--reboot if-required", arguments);
            Assert.Contains("--category Security", arguments);
            Assert.Equal(0, Run("schedule", "delete"));
            Assert.False(RunJson("schedule").GetProperty("exists").GetBoolean());
            Assert.Equal(ExitCodes.Usage, Run("schedule", "create", "--frequency", "daily", "--day", "monday"));
        }

        [Fact]
        public void Menu_ScanAndInstallAll()
        {
            // 2 = scan (answer "n" to optional), Enter; 3 = install all ("y"), decline restart ("n"), Enter; 0 = exit
            Console.SetIn(new StringReader("2\nn\n\n3\ny\nn\n\n0\n"));
            Assert.Equal(0, Run("menu"));
            string text = output.ToString();
            Assert.Contains("SIMULATION MODE", text);
            Assert.Contains("KB5065432", text);
            Assert.Contains("Installation summary", text);
            Assert.Contains("Restart pending: YES", text);
        }

        [Fact]
        public void Menu_EndOfInput_Exits()
        {
            Console.SetIn(new StringReader(string.Empty));
            Assert.Equal(0, Run());
        }
    }
}
