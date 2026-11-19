using Lumyte.Samples.Framework;
using Spectre.Console.Testing;
using Xunit;

namespace Lumyte.Samples.Tests;

/// <summary>Tests launcher selection, argument isolation, and catalog coverage.</summary>
public sealed class LauncherTests
{
    /// <summary>TreeView selects the public headless hierarchy consumer.</summary>
    [Fact]
    public void TreeViewSampleSelectsHeadlessConsumer()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "ui-treeview"]), console);
        Assert.Equal("ui-treeview", sample.Id);
        Assert.Equal("headless", backend.Id);
        Assert.Equal("samples/Lumyte.UI.TreeView.Sample", backend.Project);
        Assert.Empty(console.Output);
    }

    /// <summary>Dialogs and notifications use the standard headless application consumer.</summary>
    [Fact]
    public void DialogsSampleSelectsHeadlessConsumer()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "ui-dialogs"]), console);
        Assert.Equal("ui-dialogs", sample.Id);
        Assert.Equal("headless", backend.Id);
        Assert.Equal("samples/Lumyte.UI.Dialogs.Sample", backend.Project);
        Assert.Empty(console.Output);
    }

    /// <summary>Menus and tooltips use the actual headless public consumer.</summary>
    [Fact]
    public void MenusSampleSelectsHeadlessConsumer()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "ui-menus"]), console);
        Assert.Equal("ui-menus", sample.Id);
        Assert.Equal("headless", backend.Id);
        Assert.Equal("samples/Lumyte.UI.Menus.Sample", backend.Project);
        Assert.Empty(console.Output);
    }

    /// <summary>Retained tabs use the actual headless public consumer.</summary>
    [Fact]
    public void TabsSampleSelectsHeadlessConsumer()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "ui-tabs"]), console);
        Assert.Equal("ui-tabs", sample.Id);
        Assert.Equal("headless", backend.Id);
        Assert.Equal("samples/Lumyte.UI.Tabs.Sample", backend.Project);
        Assert.Empty(console.Output);
    }

    /// <summary>Collections use the actual headless public consumer.</summary>
    [Fact]
    public void CollectionsSampleSelectsHeadlessConsumer()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "ui-collections"]), console);
        Assert.Equal("ui-collections", sample.Id);
        Assert.Equal("headless", backend.Id);
        Assert.Equal("samples/Lumyte.UI.Collections.Sample", backend.Project);
        Assert.Empty(console.Output);
    }

    /// <summary>Verifies the launcher contract.</summary>
    [Fact]
    public void SeparatorPreservesArgumentsIncludingSpacesAndLauncherOptionNames()
    {
        var options = SampleOptions.Parse(["--sample", "input", "--", "a path/settings.json", "--backend", "literal"]);
        Assert.Equal("input", options.Sample);
        Assert.Null(options.Backend);
        Assert.Equal(["a path/settings.json", "--backend", "literal"], options.Arguments);
    }

    /// <summary>Verifies invalid input is rejected.</summary>
    /// <param name="arguments">Invalid arguments.</param>
    [Theory]
    [InlineData("--unknown")]
    [InlineData("--sample")]
    [InlineData("--port", "0")]
    [InlineData("--port", "65536")]
    [InlineData("--configuration", "invalid")]
    [InlineData("--sample", "--backend")]
    [InlineData("--sample", "audio", "--sample", "graphics")]
    public void InvalidOptionsFailBeforeLaunching(params string[] arguments) => Assert.Throws<ArgumentException>(() => SampleOptions.Parse(arguments));

    /// <summary>Verifies the launcher contract.</summary>
    [Fact]
    public void ExplicitSelectionDoesNotPrompt()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "audio", "--backend", "webaudio"]), console);
        Assert.Equal("audio", sample.Id);
        Assert.True(backend.Browser);
        Assert.Empty(console.Output);
    }

    /// <summary>Verifies the public geometry consumer is selectable without prompting.</summary>
    [Fact]
    public void MathematicsSampleSelectsTheManagedPublicConsumer()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "mathematics"]), console);
        Assert.Equal("mathematics", sample.Id);
        Assert.Equal("managed", backend.Id);
        Assert.Equal("samples/Lumyte.Mathematics.Sample", backend.Project);
        Assert.False(backend.Browser);
        Assert.Empty(console.Output);
    }

    /// <summary>Verifies the fixed local map consumer is registered in the real launcher.</summary>
    [Fact]
    public void MapsSampleSelectsTheHeadlessPublicConsumer()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "maps", "--backend", "headless"]), console);
        Assert.Equal("maps", sample.Id);
        Assert.Equal("samples/Lumyte.Maps.Sample", backend.Project);
        Assert.Equal(["headless"], backend.Arguments);
        Assert.False(backend.Browser);
        Assert.Empty(console.Output);
    }

    /// <summary>Verifies the portable bounded memory map consumer uses browser publishing.</summary>
    [Fact]
    public void MapsSampleSelectsTheBrowserConsumer()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "maps", "--backend", "browser"]), console);
        Assert.Equal("maps", sample.Id);
        Assert.Equal("samples/Lumyte.Maps.Browser.Sample", backend.Project);
        Assert.True(backend.Browser);
        Assert.Empty(backend.Arguments);
        Assert.Empty(console.Output);
    }

    /// <summary>Verifies borrowed media is selectable through the headless host.</summary>
    [Fact]
    public void MediaSampleSelectsTheHeadlessConsumer()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "ui-media"]), console);
        Assert.Equal("ui-media", sample.Id);
        Assert.Equal("headless", backend.Id);
        Assert.Equal("samples/Lumyte.UI.Media.Sample", backend.Project);
        Assert.False(backend.Browser);
        Assert.Empty(console.Output);
    }

    /// <summary>Verifies core layout is selectable without font or device arguments.</summary>
    [Fact]
    public void LayoutSampleSelectsTheHeadlessConsumer()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "ui-layout"]), console);
        Assert.Equal("ui-layout", sample.Id);
        Assert.Equal("headless", backend.Id);
        Assert.Equal("samples/Lumyte.UI.Layout.Sample", backend.Project);
        Assert.False(backend.Browser);
        Assert.Empty(console.Output);
    }

    /// <summary>Verifies scroll is selectable through the headless host.</summary>
    [Fact]
    public void ScrollSampleSelectsTheHeadlessConsumer()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "ui-scroll"]), console);
        Assert.Equal("ui-scroll", sample.Id);
        Assert.Equal("headless", backend.Id);
        Assert.Equal("samples/Lumyte.UI.Scroll.Sample", backend.Project);
        Assert.False(backend.Browser);
        Assert.Empty(console.Output);
    }

    /// <summary>Verifies numeric editing is selectable through the ordinary headless application.</summary>
    [Fact]
    public void NumericSampleSelectsTheHeadlessConsumer()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "ui-numeric"]), console);
        Assert.Equal("ui-numeric", sample.Id);
        Assert.Equal("headless", backend.Id);
        Assert.Equal("samples/Lumyte.UI.Numeric.Sample", backend.Project);
        Assert.False(backend.Browser);
        Assert.Empty(console.Output);
    }

    /// <summary>Verifies controls is selectable through the headless host.</summary>
    [Fact]
    public void ControlsSampleSelectsTheHeadlessConsumer()
    {
        using var console = new TestConsole();
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "ui-controls"]), console);
        Assert.Equal("ui-controls", sample.Id);
        Assert.Equal("headless", backend.Id);
        Assert.Equal("samples/Lumyte.UI.Controls.Sample", backend.Project);
        Assert.False(backend.Browser);
        Assert.Empty(console.Output);
    }

    /// <summary>Verifies the launcher contract.</summary>
    [Fact]
    public void MissingBackendPromptsOnlyForCompatibleBackends()
    {
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Input.PushKey(ConsoleKey.DownArrow);
        console.Input.PushKey(ConsoleKey.Enter);
        (_, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "audio"]), console);
        Assert.Equal("webaudio", backend.Id);
        Assert.Contains("openal", console.Output);
        Assert.DoesNotContain("vulkan", console.Output);
    }

    /// <summary>Verifies the launcher contract.</summary>
    [Fact]
    public void BackendFirstFiltersSampleChoices()
    {
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = true;
        console.Input.PushKey(ConsoleKey.Enter);
        (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--backend", "openal"]), console);
        Assert.Equal("audio", sample.Id);
        Assert.Equal("openal", backend.Id);
        Assert.DoesNotContain("graphics", console.Output);
    }

    /// <summary>Verifies the launcher contract.</summary>
    [Fact]
    public void NonInteractiveMissingSelectionFailsWithoutWaiting()
    {
        using var console = new TestConsole();
        console.Profile.Capabilities.Interactive = false;
        Assert.Throws<ArgumentException>(() => SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse([]), console));
        Assert.Throws<ArgumentException>(() => SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "audio"]), console));
    }

    /// <summary>Verifies invalid input is rejected.</summary>
    /// <param name="sample">Requested sample.</param>
    /// <param name="backend">Requested backend.</param>
    [Theory]
    [InlineData("missing", "managed")]
    [InlineData("audio", "vulkan")]
    public void InvalidPairIsRejected(string sample, string backend)
    {
        using var console = new TestConsole();
        Assert.Throws<ArgumentException>(() => SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", sample, "--backend", backend]), console));
    }

    /// <summary>Verifies the launcher contract.</summary>
    [Fact]
    public void CatalogCoversEveryExecutableProjectExactlyAsALogicalSample()
    {
        string root = SampleHost.FindRoot(AppContext.BaseDirectory);
        string[] projects = Directory.GetFiles(Path.Combine(root, "samples"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".Sample.csproj", StringComparison.Ordinal))
            .Select(path => Path.GetFileNameWithoutExtension(path)).Order().ToArray();
        string[] registered = SampleCatalog.All.SelectMany(sample => sample.Backends)
            .Select(backend => Path.GetFileName(backend.Project)).Distinct().Order().ToArray();
        Assert.Equal(projects, registered);
        Assert.Equal(SampleCatalog.All.Count, SampleCatalog.All.Select(sample => sample.Id).Distinct().Count());
        foreach (SampleDefinition sample in SampleCatalog.All)
        {
            Assert.Equal(sample.Backends.Length, sample.Backends.Select(backend => backend.Id).Distinct().Count());
        }
    }
}
