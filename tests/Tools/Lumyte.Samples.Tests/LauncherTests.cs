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
