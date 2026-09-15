using System;
using System.Text.Json;
using WinUpdateManager.Cli;
using WinUpdateManager.Core;
using WinUpdateManager.Output;
using WinUpdateManager.Simulation;
using Xunit;

namespace WinUpdateManager.Tests
{
    public class UpdateFilterTests
    {
        private static UpdateInfo Update(string kb, string classification = "Security Updates", string severity = "Critical", decimal mb = 10, bool optional = false, UpdateKind kind = UpdateKind.Software, string title = null)
        {
            var u = new UpdateInfo { Id = Guid.NewGuid().ToString(), Title = title ?? "Update (KB" + kb + ")", Severity = severity, MaxDownloadSize = mb * 1024 * 1024, IsOptional = optional, Kind = kind };
            if (kb != null) u.KbArticleIds.Add(kb);
            if (classification != null) u.Classifications.Add(classification);
            return u;
        }

        private static UpdateFilter Filter(params string[] args)
        {
            var all = new string[args.Length + 1];
            all[0] = "list";
            Array.Copy(args, 0, all, 1, args.Length);
            return UpdateFilter.FromArgs(CommandLineParser.Parse(all));
        }

        [Fact]
        public void Default_ExcludesOptionalAndDrivers()
        {
            var f = new UpdateFilter();
            Assert.True(f.Matches(Update("1000001")));
            Assert.False(f.Matches(Update("1000002", optional: true)));
            Assert.False(f.Matches(Update(null, "Drivers", kind: UpdateKind.Driver)));
        }

        [Fact]
        public void ExplicitKb_OverridesOptionalExclusion()
        {
            var f = Filter("--kb", "KB1000002");
            Assert.True(f.Matches(Update("1000002", optional: true)));
            Assert.False(f.Matches(Update("1000001")));
        }

        [Fact]
        public void ExcludeKb()
        {
            var f = Filter("--exclude-kb", "1000001");
            Assert.False(f.Matches(Update("1000001")));
            Assert.True(f.Matches(Update("1000003")));
        }

        [Fact]
        public void Category_IsCaseInsensitiveContains()
        {
            var f = Filter("--category", "security,definition");
            Assert.True(f.Matches(Update("1", "Security Updates")));
            Assert.True(f.Matches(Update("2", "Definition Updates")));
            Assert.False(f.Matches(Update("3", "Update Rollups")));
        }

        [Fact]
        public void Severity_UnspecifiedMatchesNull()
        {
            var f = Filter("--severity", "Critical,Unspecified");
            Assert.True(f.Matches(Update("1", severity: "Critical")));
            Assert.True(f.Matches(Update("2", severity: null)));
            Assert.False(f.Matches(Update("3", severity: "Important")));
        }

        [Fact]
        public void TitleIncludeExclude()
        {
            var f = Filter("--title", "Cumulative", "--exclude-title", "Preview");
            Assert.True(f.Matches(Update("1", title: "2026-09 Cumulative Update")));
            Assert.False(f.Matches(Update("2", title: "2026-09 Cumulative Update Preview")));
            Assert.False(f.Matches(Update("3", title: "Defender update")));
        }

        [Fact]
        public void MaxSize()
        {
            var f = Filter("--max-size-mb", "100");
            Assert.True(f.Matches(Update("1", mb: 99)));
            Assert.False(f.Matches(Update("2", mb: 101)));
        }

        [Fact]
        public void InvalidKb_Throws()
        {
            Assert.Throws<UsageException>(() => Filter("--kb", "KBabc"));
            Assert.Throws<UsageException>(() => Filter("--max-size-mb", "-1"));
        }

        [Fact]
        public void HasCriteria()
        {
            Assert.False(new UpdateFilter().HasCriteria);
            Assert.True(Filter("--title", "x").HasCriteria);
        }
    }

    public class SearchCriteriaTests
    {
        [Fact]
        public void BuildsCriteria()
        {
            Assert.Equal("IsInstalled=0 and IsHidden=0 and Type='Software'", SearchCriteria.Build(new SearchOptions()));
            Assert.Equal("IsInstalled=0 and IsHidden=0", SearchCriteria.Build(new SearchOptions { IncludeDrivers = true }));
            Assert.Equal("IsInstalled=0 and IsHidden=1 and Type='Software'", SearchCriteria.Build(new SearchOptions { Scope = SearchScope.Hidden }));
            Assert.Equal("IsInstalled=1", SearchCriteria.Build(new SearchOptions { Scope = SearchScope.Installed, IncludeDrivers = true }));
            Assert.Equal("IsAssigned=1", SearchCriteria.Build(new SearchOptions { RawCriteria = " IsAssigned=1 " }));
        }

        [Theory]
        [InlineData("wsus", UpdateSource.Wsus)]
        [InlineData("MU", UpdateSource.MicrosoftUpdate)]
        [InlineData("windowsupdate", UpdateSource.WindowsUpdate)]
        [InlineData(null, UpdateSource.Default)]
        public void ParsesSource(string value, UpdateSource expected)
        {
            Assert.Equal(expected, SearchCriteria.ParseSource(value));
        }

        [Fact]
        public void InvalidSource_Throws()
        {
            Assert.Throws<UsageException>(() => SearchCriteria.ParseSource("internet"));
        }
    }

    public class PolicyTests
    {
        [Fact]
        public void Disable_SetsNoAutoUpdate()
        {
            var p = new PolicyChange { Mode = 1 }.ApplyTo(new AutoUpdatePolicy { AUOptions = 4 });
            Assert.Equal(1, p.NoAutoUpdate);
            Assert.StartsWith("Disabled", p.DescribeMode());
        }

        [Fact]
        public void Schedule_ImpliesScheduledInstallMode()
        {
            var p = new PolicyChange { Day = 1, Hour = 4 }.ApplyTo(new AutoUpdatePolicy { AUOptions = 3 });
            Assert.Equal(4, p.AUOptions);
            Assert.Equal(0, p.NoAutoUpdate);
            Assert.Equal(1, p.ScheduledInstallDay);
            Assert.Equal(4, p.ScheduledInstallTime);
            Assert.Contains("Sunday at 04:00", p.DescribeMode());
        }

        [Fact]
        public void ScheduledMode_GetsDefaultSchedule()
        {
            var p = new PolicyChange { Mode = 4 }.ApplyTo(new AutoUpdatePolicy());
            Assert.Equal(0, p.ScheduledInstallDay);
            Assert.Equal(3, p.ScheduledInstallTime);
        }

        [Fact]
        public void Wsus_SetAndClear()
        {
            var p = new PolicyChange { WsusServer = "http://wsus01:8530", TargetGroup = "Servers" }.ApplyTo(new AutoUpdatePolicy());
            Assert.True(p.UsesWsus);
            Assert.Equal("http://wsus01:8530", p.WUStatusServer);
            Assert.Equal(1, p.TargetGroupEnabled);
            Assert.Equal("WSUS (http://wsus01:8530)", p.DescribeSource());

            var cleared = new PolicyChange { ClearWsus = true }.ApplyTo(p);
            Assert.False(cleared.UsesWsus);
            Assert.Null(cleared.WUServer);
            Assert.Null(cleared.UseWUServer);
            Assert.Null(cleared.TargetGroup);
            Assert.False(p.Clone() == p);
        }

        [Fact]
        public void ResetAll_ClearsEverything()
        {
            var p = new PolicyChange { ResetAll = true }.ApplyTo(new AutoUpdatePolicy { AUOptions = 4, WUServer = "http://x", UseWUServer = 1 });
            Assert.False(p.IsConfigured);
        }

        [Theory]
        [InlineData("disabled", 1)]
        [InlineData("notify", 2)]
        [InlineData("download", 3)]
        [InlineData("scheduled", 4)]
        [InlineData("5", 5)]
        public void ParseMode(string input, int expected)
        {
            Assert.Equal(expected, PolicyParsing.ParseMode(input));
        }

        [Theory]
        [InlineData("every", 0)]
        [InlineData("sun", 1)]
        [InlineData("Monday", 2)]
        [InlineData("sat", 7)]
        [InlineData("3", 3)]
        public void ParseDay(string input, int expected)
        {
            Assert.Equal(expected, PolicyParsing.ParseDay(input));
        }

        [Fact]
        public void ParseDay_Invalid()
        {
            Assert.Throws<UsageException>(() => PolicyParsing.ParseDay("someday"));
            Assert.Throws<UsageException>(() => PolicyParsing.ParseDay("s"));
        }

        [Fact]
        public void ParseTime()
        {
            int h, m;
            PolicyParsing.ParseTime("03:30", out h, out m);
            Assert.Equal(3, h);
            Assert.Equal(30, m);
            PolicyParsing.ParseTime("22", out h, out m);
            Assert.Equal(22, h);
            Assert.Equal(0, m);
            Assert.Throws<UsageException>(() => PolicyParsing.ParseTime("24:00", out h, out m));
            Assert.Throws<UsageException>(() => PolicyParsing.ParseTime("3:61", out h, out m));
        }

        [Fact]
        public void ValidateServerUrl()
        {
            PolicyParsing.ValidateServerUrl("https://wsus.contoso.local:8531", "--wsus-server");
            Assert.Throws<UsageException>(() => PolicyParsing.ValidateServerUrl("wsus01:8530", "--wsus-server"));
            Assert.Throws<UsageException>(() => PolicyParsing.ValidateServerUrl("ftp://wsus01", "--wsus-server"));
        }

        [Fact]
        public void RebootMode()
        {
            Assert.Equal(Core.RebootMode.IfRequired, PolicyParsing.ParseRebootMode("if-required", Core.RebootMode.Never));
            Assert.Equal(Core.RebootMode.Never, PolicyParsing.ParseRebootMode(null, Core.RebootMode.Never));
            Assert.Throws<UsageException>(() => PolicyParsing.ParseRebootMode("sometimes", Core.RebootMode.Never));
        }
    }

    public class OsCatalogTests
    {
        [Theory]
        [InlineData(6002, "Windows Server 2008")]
        [InlineData(7601, "Windows Server 2008 R2")]
        [InlineData(9200, "Windows Server 2012")]
        [InlineData(9600, "Windows Server 2012 R2")]
        [InlineData(14393, "Windows Server 2016")]
        [InlineData(17763, "Windows Server 2019")]
        [InlineData(20348, "Windows Server 2022")]
        [InlineData(25398, "Windows Server, version 23H2")]
        [InlineData(26100, "Windows Server 2025")]
        public void ServerFamilies(int build, string expected)
        {
            Assert.Equal(expected, OsCatalog.GetFamily(build, true));
        }

        [Fact]
        public void ClientFamilies()
        {
            Assert.Equal("Windows 10", OsCatalog.GetFamily(19045, false));
            Assert.Equal("Windows 11", OsCatalog.GetFamily(26100, false));
        }

        [Fact]
        public void SupportDates()
        {
            Assert.Equal(new DateTime(2027, 1, 12), OsCatalog.GetExtendedSupportEnd(14393, true));
            Assert.Null(OsCatalog.GetExtendedSupportEnd(19045, false));
        }
    }

    public class WuaErrorsTests
    {
        [Fact]
        public void KnownCodes()
        {
            Assert.Contains("WU_E_PT_WINHTTP_NAME_NOT_RESOLVED", WuaErrors.Describe(unchecked((int)0x8024402C)));
            Assert.StartsWith("0x80070422 - ", WuaErrors.Format(unchecked((int)0x80070422)));
        }

        [Fact]
        public void UnknownWuaCode()
        {
            Assert.Contains("Windows Update Agent error", WuaErrors.Describe(unchecked((int)0x8024FFFF)));
        }
    }

    public class JsonWriterTests
    {
        [Fact]
        public void ProducesValidNestedJson()
        {
            var w = new JsonWriter().BeginObject()
                .Property("text", "quote \" backslash \\ newline \n tab \t ctrl ")
                .Property("number", 42)
                .Property("decimal", 1.5m)
                .Property("flag", true)
                .Property("nullable", (int?)null)
                .Property("date", (DateTime?)new DateTime(2026, 9, 15, 3, 0, 0, DateTimeKind.Utc))
                .Property("list", new[] { "a", "b" })
                .BeginArray("empty").EndArray()
                .BeginArray("objects")
                .BeginObject().Property("x", 1).EndObject()
                .BeginObject().EndObject()
                .EndArray()
                .EndObject();

            using (var doc = JsonDocument.Parse(w.ToString()))
            {
                var root = doc.RootElement;
                Assert.Equal("quote \" backslash \\ newline \n tab \t ctrl ", root.GetProperty("text").GetString());
                Assert.Equal(42, root.GetProperty("number").GetInt32());
                Assert.Equal(1.5m, root.GetProperty("decimal").GetDecimal());
                Assert.True(root.GetProperty("flag").GetBoolean());
                Assert.Equal(JsonValueKind.Null, root.GetProperty("nullable").ValueKind);
                Assert.Equal("2026-09-15T03:00:00Z", root.GetProperty("date").GetString());
                Assert.Equal(2, root.GetProperty("list").GetArrayLength());
                Assert.Equal(0, root.GetProperty("empty").GetArrayLength());
                Assert.Equal(1, root.GetProperty("objects")[0].GetProperty("x").GetInt32());
            }
        }
    }

    public class ScheduleTests
    {
        [Fact]
        public void NextRun_Weekly()
        {
            var monday = new DateTime(2026, 9, 14, 10, 0, 0); // Monday
            var def = new ScheduleDefinition { Frequency = ScheduleFrequency.Weekly, Day = DayOfWeek.Sunday, Hour = 3 };
            Assert.Equal(new DateTime(2026, 9, 20, 3, 0, 0), SimulatedTaskScheduler.NextRun(def, monday));

            def.Day = DayOfWeek.Monday;
            Assert.Equal(new DateTime(2026, 9, 21, 3, 0, 0), SimulatedTaskScheduler.NextRun(def, monday));
        }

        [Fact]
        public void NextRun_Daily()
        {
            var now = new DateTime(2026, 9, 14, 2, 0, 0);
            var def = new ScheduleDefinition { Frequency = ScheduleFrequency.Daily, Hour = 3, Minute = 15 };
            Assert.Equal(new DateTime(2026, 9, 14, 3, 15, 0), SimulatedTaskScheduler.NextRun(def, now));
            Assert.Equal("Every day at 03:15", def.Describe());
        }

        [Fact]
        public void ScheduledArguments_IncludeFiltersAndReboot()
        {
            var a = CommandLineParser.Parse(new[] { "schedule", "create", "--category", "Security Updates", "--exclude-title", "Preview", "--include-optional" });
            string args = App.BuildScheduledArguments(a, Core.RebootMode.IfRequired, 300);
            Assert.Equal("install --yes --no-color --reboot if-required --reboot-delay 300 --category \"Security Updates\" --exclude-title Preview --include-optional", args);
        }
    }
}
