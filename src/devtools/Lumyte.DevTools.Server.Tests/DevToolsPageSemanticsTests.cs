using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lumyte.DevTools.Server.Tests;

public sealed partial class DevToolsPageSemanticsTests
{
    private static readonly string ServerDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Lumyte.DevTools.Server"));
    private static readonly string Page = Path.Combine(ServerDirectory, "wwwroot", "index.html");

    [Fact]
    public void ProductionShellLoadsHashedModuleAndStyles()
    {
        string html = File.ReadAllText(Page);
        Match script = ScriptRegex().Match(html);
        Match style = StyleRegex().Match(html);

        Assert.True(script.Success, "The production shell should reference a hashed JavaScript module.");
        Assert.True(style.Success, "The production shell should reference a hashed stylesheet.");
        Assert.True(File.Exists(Path.Combine(ServerDirectory, "wwwroot", script.Groups[1].Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))));
        Assert.True(File.Exists(Path.Combine(ServerDirectory, "wwwroot", style.Groups[1].Value.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public void ProductionPathContainsOnlyViteOutput()
    {
        string[] names = Directory.GetFiles(Path.Combine(ServerDirectory, "wwwroot")).Select(Path.GetFileName).ToArray()!;

        Assert.Equal(["index.html"], names);
        Assert.False(File.Exists(Path.Combine(ServerDirectory, "wwwroot", "app.js")));
        Assert.False(File.Exists(Path.Combine(ServerDirectory, "wwwroot", "styles.css")));
    }

    [Fact]
    public void FrontendSourceAndLockfileRemainReproducible()
    {
        string client = Path.Combine(ServerDirectory, "ClientApp");
        using JsonDocument package = JsonDocument.Parse(File.ReadAllText(Path.Combine(client, "package.json")));

        Assert.Equal("9.74.7", package.RootElement.GetProperty("dependencies").GetProperty("@fluentui/react-components").GetString());
        Assert.True(File.Exists(Path.Combine(client, "package-lock.json")));
        Assert.True(File.Exists(Path.Combine(client, "src", "protocol", "transport.test.ts")));
    }

    [GeneratedRegex("<script[^>]+type=\"module\"[^>]+src=\"([^\"]*assets/index-[^\"]+\\.js)\"")]
    private static partial Regex ScriptRegex();
    [GeneratedRegex("<link[^>]+href=\"([^\"]*assets/index-[^\"]+\\.css)\"")]
    private static partial Regex StyleRegex();
}