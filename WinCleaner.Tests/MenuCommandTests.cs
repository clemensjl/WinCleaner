using WinCleaner.Commands;
using CommandContext = WinCleaner.Commands.CommandContext;
using Logger = WinCleaner.Core.Logger;

namespace WinCleaner.Tests;

public class MenuCommandTests
{
    [Theory]
    [InlineData("scan-junk", new[] { "scan-junk" })]
    [InlineData("analyze-disk C:\\ --by-type", new[] { "analyze-disk", "C:\\", "--by-type" })]
    [InlineData("uninstall \"7-Zip 26.01 (x64)\"", new[] { "uninstall", "7-Zip 26.01 (x64)" })]
    [InlineData("   spaces    collapse  ", new[] { "spaces", "collapse" })]
    [InlineData("", new string[0])]
    public void SplitArgs_ParsesQuotedAndUnquoted(string line, string[] expected)
        => Assert.Equal(expected, MenuCommand.SplitArgs(line));

    [Fact]
    public void Menu_IsDiscoveredByRegistry()
        => Assert.NotNull(CommandRegistry.Find("menu"));

    [Fact]
    public void Execute_WithRedirectedInput_RefusesGracefully()
    {
        // Im Testlauf ist die Konsoleneingabe umgeleitet -> das Menü darf nicht
        // in eine Leseschleife laufen, sondern muss mit Hinweis abbrechen.
        var ctx = new CommandContext
        {
            Args = System.Array.Empty<string>(),
            FullArgs = new[] { "menu" },
            Logger = new Logger(),
            Json = false
        };
        Assert.Equal(1, new MenuCommand().Execute(ctx));
    }
}
