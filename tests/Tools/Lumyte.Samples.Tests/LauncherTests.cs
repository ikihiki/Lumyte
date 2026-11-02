using Lumyte.Samples.Framework;
using Spectre.Console.Testing;
using Xunit;

namespace Lumyte.Samples.Tests;

/// <summary>Tests launcher selection, argument isolation, and catalog coverage.</summary>
public sealed class LauncherTests
{
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

    /// <summary>Verifies UI and remote automation adapters preserve their required argument prefixes.</summary>
    /// <param name="sampleId">The logical sample.</param>
    /// <param name="backendId">The selected adapter.</param>
    /// <param name="project">The target project name.</param>
    /// <param name="argument">The optional required first argument.</param>
    [Theory]
    [InlineData("ui", "record-only", "UI", "record-only")]
    [InlineData("ui", "vulkan", "UI", "vulkan")]
    [InlineData("ui", "wgpu", "UI", "wgpu")]
    [InlineData("ui-popup", "none", "UI.Popup", "none")]
    [InlineData("ui-popup", "vulkan", "UI.Popup", "vulkan")]
    [InlineData("ui-popup", "wgpu", "UI.Popup", "wgpu")]
    [InlineData("diagnostics-ui-game", "managed", "Diagnostics.UI.Automation.Game", null)]
    [InlineData("diagnostics-ui-operator", "managed", "Diagnostics.UI.Automation.Operator", null)]
    public void UiAndDiagnosticsAdaptersPreserveArguments(string sampleId, string backendId, string project, string? argument)
    {
        using var console = new TestConsole();
        (_, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", sampleId, "--backend", backendId]), console);
        Assert.Equal($"samples/Lumyte.{project}.Sample", backend.Project);
        Assert.Equal(argument is null ? [] : new[] { argument }, backend.Arguments);
        Assert.False(backend.Browser);
    }

    /// <summary>Verifies browser serving and Windows text input retain their platform contracts.</summary>
    [Fact]
    public void UiBrowserAndTextEditingHaveCorrectHostRequirements()
    {
        SampleBackend browser = Assert.Single(SampleCatalog.All.Single(sample => sample.Id == "ui").Backends, backend => backend.Id == "browser");
        Assert.True(browser.Browser);
        Assert.False(browser.WindowsOnly);
        Assert.Equal("samples/Lumyte.UI.Browser.Sample", browser.Project);
        Assert.Empty(browser.Arguments);
        SampleBackend text = Assert.Single(SampleCatalog.All.Single(sample => sample.Id == "ui-text-editing").Backends);
        Assert.True(text.WindowsOnly);
        Assert.Equal("samples/Lumyte.UI.TextEditing.Sample", text.Project);
        Assert.Empty(text.Arguments);
        if (!OperatingSystem.IsWindows())
        {
            using var console = new TestConsole();
            Assert.Throws<ArgumentException>(() => SampleSelector.Select(SampleCatalog.All, SampleOptions.Parse(["--sample", "ui-text-editing", "--backend", "windows"]), console));
        }
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
