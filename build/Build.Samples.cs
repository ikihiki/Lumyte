using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    [Parameter("Independent balanced NativeAOT group: balanced-1 or balanced-2. Omit for all 14 publications.")]
    public string? AotGroup { get; set; }

    private void ValidateAotSelection()
    {
        if (AotGroup is not null && (Suite != "aot" || AotGroup is not ("balanced-1" or "balanced-2")))
        {
            throw new ArgumentException("--aot-group requires --suite aot and balanced-1 or balanced-2.");
        }
    }

    private Task TestAotAsync() => RunAotSamplesAsync();

    private async Task PublishWindowSampleAsync()
    {
        await DotNetAsync("publish", "samples/Lumyte.Platform.Windows.Sample", "-c", Configuration, "--no-build", "--no-restore",
            "-o", Path.Combine(ArtifactRoot, "single-window-sample")).ConfigureAwait(false);
    }

    private async Task RunSamplesAsync()
    {
        const string Audio = "tests/Audio/Lumyte.Resources.Audio.IntegrationTests";
        foreach (string? scenario in new[] { "--reload", null, "--recover", "--recover-stop" })
        {
            foreach (string[] mode in _invocationModes)
            {
                await RunManagedSampleAsync(Audio, scenario is null ? mode : [scenario, .. mode]).ConfigureAwait(false);
            }
        }

        await RunManagedSampleAsync(Audio, "--recover-start-cancel", "--host").ConfigureAwait(false);
        await RunManagedSampleAsync(Audio, "--recover-start-cancel", "--host", "--di").ConfigureAwait(false);
        foreach (string[] mode in _invocationModes)
        {
            await RunManagedSampleAsync("samples/Lumyte.Execution.Sample", mode).ConfigureAwait(false);
        }

        foreach (string project in new[]
        {
            "samples/Lumyte.Signals.Sample",
            "samples/Lumyte.Composition.Sample", "samples/Lumyte.Resources.Sample", "samples/Lumyte.Diagnostics.Sample",
            "samples/Lumyte.Animation.Sample", "samples/Lumyte.StateMachines.Sample", "samples/Lumyte.Animation.StateMachine.Sample",
            "samples/Lumyte.UI.Functions.Sample", "samples/Lumyte.UI.Layout.Sample",
        })
        {
            await RunManagedSampleAsync(project).ConfigureAwait(false);
        }

        Directory.CreateDirectory(ArtifactRoot);
        string settings = Path.Combine(ArtifactRoot, "sample-input-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await RunManagedSampleAsync("samples/Lumyte.Input.Advanced.Sample", settings).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(settings);
        }

        await RunManagedSampleAsync("samples/Lumyte.UI.Collections.Sample", "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        await RunOpenAlSampleAsync("samples/Lumyte.Audio.Sample").ConfigureAwait(false);
        string[][] openAlInvocations = [["--openal"], ["--openal", "--host"], ["--openal", "--reload"], ["--openal", "--host", "--reload"]];
        foreach (string[] arguments in openAlInvocations)
        {
            await RunOpenAlSampleAsync(Audio, arguments).ConfigureAwait(false);
        }

        foreach (string[] mode in _invocationModes)
        {
            await RunOpenAlSampleAsync("samples/Lumyte.Audio.Execution.Sample", mode).ConfigureAwait(false);
        }
    }

    private sealed record AotSample(string Group, string Project, string Directory, string Executable, bool Strict, string[][] Modes, bool OpenAl = false);

    private static readonly string[][] _aotDefaultMode = [[]];
    private static readonly string[][] _aotHostModes = [[], ["--host"]];
    private static readonly string[][] _aotFontMode = [["tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf"]];

    // Hosted aot.log publication costs: approximately 460s / 457s (921s whole suite).
    // Mix expensive UI publications with utilities, rather than splitting by project count.
    private static AotSample[] AotSamples =>
    [
        new("balanced-1", "samples/Lumyte.Execution.Sample", "execution-aot", "Lumyte.Execution.Sample", false, _invocationModes),
        new("balanced-1", "samples/Lumyte.Signals.Sample", "signals-aot", "Lumyte.Signals.Sample", true, _aotDefaultMode),
        new("balanced-1", "tests/Core/Lumyte.Resources.AotSmoke", "resources-aot", "Lumyte.Resources.AotSmoke", false, _aotHostModes),
        new("balanced-1", "tests/Graphics/Lumyte.Resources.Graphics.AotSmoke", "resource-graphics-aot", "Lumyte.Resources.Graphics.AotSmoke", true, _aotHostModes),
        new("balanced-2", "samples/Lumyte.UI.Application.Headless.Sample", "ui-application-aot", "Lumyte.UI.Application.Headless.Sample", true, _aotDefaultMode),
        new("balanced-1", "samples/Lumyte.UI.Navigation.Sample", "ui-navigation-aot", "Lumyte.UI.Navigation.Sample", true, _aotDefaultMode),
        new("balanced-2", "samples/Lumyte.UI.Layout.Sample", "ui-layout-aot", "Lumyte.UI.Layout.Sample", true, _aotDefaultMode),
        new("balanced-1", "samples/Lumyte.UI.Functions.Sample", "ui-functions-aot", "Lumyte.UI.Functions.Sample", true, _aotDefaultMode),
        new("balanced-1", "samples/Lumyte.UI.Media.Sample", "ui-media-aot", "Lumyte.UI.Media.Sample", true, _aotFontMode),
        new("balanced-2", "samples/Lumyte.UI.Controls.Sample", "ui-controls-aot", "Lumyte.UI.Controls.Sample", true, _aotFontMode),
        new("balanced-2", "samples/Lumyte.UI.Collections.Sample", "ui-collections-aot", "Lumyte.UI.Collections.Sample", true, _aotFontMode),
        new("balanced-1", "samples/Lumyte.UI.Pages.Sample", "ui-pages-aot", "Lumyte.UI.Pages.Sample", true, _aotDefaultMode),
        new("balanced-2", "samples/Lumyte.UI.Transitions.Sample", "ui-transitions-aot", "Lumyte.UI.Transitions.Sample", true, _aotDefaultMode),
        new("balanced-1", "tests/Audio/Lumyte.Resources.Audio.IntegrationTests", "resources-audio-aot", "Lumyte.Resources.Audio.IntegrationTests", true, _invocationModes, OpenAl: true),
    ];

    private async Task RunAotSamplesAsync()
    {
        ValidateAotSelection();
        foreach (AotSample sample in AotSamples.Where(sample => AotGroup is null || sample.Group == AotGroup))
        {
            string executable = await PublishAotSampleAsync(sample.Project, sample.Directory, sample.Executable, sample.Strict).ConfigureAwait(false);
            int invocation = 0;
            foreach (string[] mode in sample.Modes)
            {
                string log = sample.Directory + "-" + (invocation++).ToString(System.Globalization.CultureInfo.InvariantCulture);
                await CaptureValidationLogAsync(log + ".log", () => ToolAsync(executable, mode)).ConfigureAwait(false);
                if (sample.OpenAl)
                {
                    await CaptureValidationLogAsync(log + "-openal.log", () => ToolWithEnvironmentAsync(executable, _openAlEnvironment, ["--openal", .. mode])).ConfigureAwait(false);
                }
            }
        }
    }

    private Task<string> RunManagedSampleAsync(string project, params string[] arguments)
        => DotNetAsync(["run", "--project", project, "-c", Configuration, "--no-build", "--no-restore", "--", .. arguments]);

    private Task<string> RunOpenAlSampleAsync(string project, params string[] arguments)
        => DotNetWithEnvironmentAsync(_openAlEnvironment, ["run", "--project", project, "-c", Configuration, "--no-build", "--no-restore", "--", .. arguments]);

    private async Task<string> PublishAotSampleAsync(string project, string directory, string executable, bool strict)
    {
        string output = Path.Combine(ArtifactRoot, directory);
        var arguments = new List<string> { "publish", project, "-c", Configuration, "-p:PublishAot=true", "-o", output };
        if (strict)
        {
            arguments.AddRange(["-p:ILLinkTreatWarningsAsErrors=true", "-p:IlcTreatWarningsAsErrors=true"]);
        }

        await CaptureValidationLogAsync(directory + "-publish.log", () => DotNetAsync(arguments.ToArray())).ConfigureAwait(false);
        return Path.Combine(output, executable + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
    }

}
