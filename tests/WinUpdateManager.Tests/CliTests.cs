using System;
using WinUpdateManager.Cli;
using WinUpdateManager.Core;
using Xunit;

namespace WinUpdateManager.Tests
{
    public class CommandLineParserTests
    {
        [Fact]
        public void NoArguments_StartsMenu()
        {
            var a = CommandLineParser.Parse(new string[0]);
            Assert.Equal("menu", a.Command);
            Assert.False(a.CommandSpecified);
        }

        [Fact]
        public void ParsesCommandValuesAndFlags()
        {
            var a = CommandLineParser.Parse(new[] { "install", "--kb", "KB5030216,5031364", "--yes", "--reboot=if-required", "--json" });
            Assert.Equal("install", a.Command);
            Assert.Equal("KB5030216,5031364", a.Get("kb"));
            Assert.True(a.Flag("yes"));
            Assert.True(a.Flag("json"));
            Assert.Equal("if-required", a.Get("reboot"));
        }

        [Theory]
        [InlineData("scan", "list")]
        [InlineData("search", "list")]
        [InlineData("update", "install")]
        [InlineData("mu", "microsoft-update")]
        [InlineData("reboot-status", "pending-reboot")]
        [InlineData("SERVICE", "services")]
        public void ResolvesAliases(string input, string expected)
        {
            Assert.Equal(expected, CommandLineParser.Parse(new[] { input }).Command);
        }

        [Fact]
        public void ShortFlags()
        {
            var a = CommandLineParser.Parse(new[] { "install", "-y", "/?" });
            Assert.True(a.Flag("yes"));
            Assert.True(a.Flag("help"));
        }

        [Fact]
        public void VersionOption()
        {
            Assert.Equal("version", CommandLineParser.Parse(new[] { "--version" }).Command);
        }

        [Fact]
        public void Positionals()
        {
            var a = CommandLineParser.Parse(new[] { "services", "startup", "wuauserv", "manual" });
            Assert.Equal("services", a.Command);
            Assert.Equal(new[] { "startup", "wuauserv", "manual" }, a.Positionals.ToArray());
        }

        [Fact]
        public void UnknownOption_SuggestsClosestName()
        {
            var ex = Assert.Throws<UsageException>(() => CommandLineParser.Parse(new[] { "list", "--kbb", "1" }));
            Assert.Contains("--kb", ex.Message);
        }

        [Fact]
        public void MissingValue_Throws()
        {
            Assert.Throws<UsageException>(() => CommandLineParser.Parse(new[] { "list", "--kb" }));
            Assert.Throws<UsageException>(() => CommandLineParser.Parse(new[] { "list", "--kb", "--json" }));
        }

        [Fact]
        public void OptionNotValidForCommand_Throws()
        {
            var ex = Assert.Throws<UsageException>(() => CommandLineParser.Parse(new[] { "status", "--kb", "123456" }));
            Assert.Contains("status", ex.Message);
        }

        [Fact]
        public void UnknownCommand_Throws()
        {
            Assert.Throws<UsageException>(() => CommandLineParser.Parse(new[] { "explode" }));
        }

        [Fact]
        public void SingleDashLongOption_Throws()
        {
            Assert.Throws<UsageException>(() => CommandLineParser.Parse(new[] { "list", "-json" }));
        }

        [Fact]
        public void GetInt_ValidatesRange()
        {
            var a = CommandLineParser.Parse(new[] { "history", "--count", "0" });
            Assert.Throws<UsageException>(() => a.GetInt("count", 30, 1, 100));
            Assert.Equal(30, CommandLineParser.Parse(new[] { "history" }).GetInt("count", 30, 1, 100));
        }
    }

    public class SelectionParserTests
    {
        [Fact]
        public void ParsesListsAndRanges()
        {
            Assert.Equal(new[] { 1, 3, 5, 6, 7 }, SelectionParser.Parse("1,3,5-7", 10).ToArray());
            Assert.Equal(new[] { 2, 3, 4 }, SelectionParser.Parse("4-2", 10).ToArray());
            Assert.Equal(new[] { 1, 2 }, SelectionParser.Parse(" 2 1 2 ", 10).ToArray());
        }

        [Fact]
        public void All()
        {
            Assert.Equal(new[] { 1, 2, 3 }, SelectionParser.Parse("all", 3).ToArray());
            Assert.Equal(new[] { 1, 2, 3 }, SelectionParser.Parse("*", 3).ToArray());
        }

        [Theory]
        [InlineData("0")]
        [InlineData("11")]
        [InlineData("abc")]
        [InlineData("")]
        [InlineData("1-99")]
        public void InvalidInput_Throws(string input)
        {
            Assert.Throws<FormatException>(() => SelectionParser.Parse(input, 10));
        }
    }

    public class TextTests
    {
        [Theory]
        [InlineData("abc", "abc")]
        [InlineData("a b", "\"a b\"")]
        [InlineData("a\"b", "\"a\\\"b\"")]
        [InlineData(@"C:\Program Files\", "\"C:\\Program Files\\\\\"")]
        [InlineData("", "\"\"")]
        public void QuoteArgument(string input, string expected)
        {
            Assert.Equal(expected, Text.QuoteArgument(input));
        }

        [Theory]
        [InlineData("KB5030216", "5030216")]
        [InlineData(" kb5030216 ", "5030216")]
        [InlineData("5030216", "5030216")]
        public void NormalizeKb(string input, string expected)
        {
            Assert.Equal(expected, Text.NormalizeKb(input));
        }

        [Fact]
        public void ExtractKb()
        {
            Assert.Equal("5065432", Text.ExtractKb("2026-09 Cumulative Update for Microsoft server operating system (KB5065432)"));
            Assert.Null(Text.ExtractKb("Intel - System - 10.1.2.3"));
        }

        [Fact]
        public void FormatSize()
        {
            Assert.Equal("-", Text.FormatSize(0));
            Assert.Equal("512 B", Text.FormatSize(512));
            Assert.Equal("1.5 MB", Text.FormatSize(1.5m * 1024 * 1024));
        }

        [Fact]
        public void Truncate()
        {
            Assert.Equal("abc", Text.Truncate("abc", 5));
            Assert.Equal("ab...", Text.Truncate("abcdefgh", 5));
        }
    }
}
