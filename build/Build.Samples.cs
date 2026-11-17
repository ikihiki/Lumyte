namespace Lumyte.Build;

internal sealed partial class Build
{
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

    private async Task RunAotSamplesAsync()
    {
        string execution = await PublishAotSampleAsync("samples/Lumyte.Execution.Sample", "execution-aot", "Lumyte.Execution.Sample", strict: false).ConfigureAwait(false);
        foreach (string[] mode in _invocationModes)
        {
            await ToolAsync(execution, mode).ConfigureAwait(false);
        }

        string signals = await PublishAotSampleAsync("samples/Lumyte.Signals.Sample", "signals-aot", "Lumyte.Signals.Sample", strict: true).ConfigureAwait(false);
        await ToolAsync(signals).ConfigureAwait(false);
        string resources = await PublishAotSampleAsync("tests/Core/Lumyte.Resources.AotSmoke", "resources-aot", "Lumyte.Resources.AotSmoke", strict: false).ConfigureAwait(false);
        await ToolAsync(resources).ConfigureAwait(false);
        await ToolAsync(resources, "--host").ConfigureAwait(false);
        string graphics = await PublishAotSampleAsync("tests/Graphics/Lumyte.Resources.Graphics.AotSmoke", "resource-graphics-aot", "Lumyte.Resources.Graphics.AotSmoke", strict: true).ConfigureAwait(false);
        await ToolAsync(graphics).ConfigureAwait(false);
        await ToolAsync(graphics, "--host").ConfigureAwait(false);
        string ui = await PublishAotSampleAsync("samples/Lumyte.UI.Application.Headless.Sample", "ui-application-aot", "Lumyte.UI.Application.Headless.Sample", strict: true).ConfigureAwait(false);
        string navigation = await PublishAotSampleAsync("samples/Lumyte.UI.Navigation.Sample", "ui-navigation-aot", "Lumyte.UI.Navigation.Sample", strict: true).ConfigureAwait(false);
        string layout = await PublishAotSampleAsync("samples/Lumyte.UI.Layout.Sample", "ui-layout-aot", "Lumyte.UI.Layout.Sample", strict: true).ConfigureAwait(false);
        await ToolAsync(layout).ConfigureAwait(false);
        string functions = await PublishAotSampleAsync("samples/Lumyte.UI.Functions.Sample", "ui-functions-aot", "Lumyte.UI.Functions.Sample", strict: true).ConfigureAwait(false);
        await ToolAsync(functions).ConfigureAwait(false);
        string media = await PublishAotSampleAsync("samples/Lumyte.UI.Media.Sample", "ui-media-aot", "Lumyte.UI.Media.Sample", strict: true).ConfigureAwait(false);
        await ToolAsync(media, "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        string controls = await PublishAotSampleAsync("samples/Lumyte.UI.Controls.Sample", "ui-controls-aot", "Lumyte.UI.Controls.Sample", strict: true).ConfigureAwait(false);
        await ToolAsync(controls, "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        string collections = await PublishAotSampleAsync("samples/Lumyte.UI.Collections.Sample", "ui-collections-aot", "Lumyte.UI.Collections.Sample", strict: true).ConfigureAwait(false);
        await ToolAsync(collections, "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        string pages = await PublishAotSampleAsync("samples/Lumyte.UI.Pages.Sample", "ui-pages-aot", "Lumyte.UI.Pages.Sample", strict: true).ConfigureAwait(false);
        await ToolAsync(pages).ConfigureAwait(false);
        string transitions = await PublishAotSampleAsync("samples/Lumyte.UI.Transitions.Sample", "ui-transitions-aot", "Lumyte.UI.Transitions.Sample", strict: true).ConfigureAwait(false);
        await ToolAsync(transitions).ConfigureAwait(false);
        await ToolAsync(navigation).ConfigureAwait(false);
        await ToolAsync(ui).ConfigureAwait(false);
        string audio = await PublishAotSampleAsync("tests/Audio/Lumyte.Resources.Audio.IntegrationTests", "resources-audio-aot", "Lumyte.Resources.Audio.IntegrationTests", strict: true).ConfigureAwait(false);
        foreach (string[] mode in _invocationModes)
        {
            await ToolAsync(audio, mode).ConfigureAwait(false);
            await ToolWithEnvironmentAsync(audio, _openAlEnvironment, ["--openal", .. mode]).ConfigureAwait(false);
        }
    }

    private Task<string> RunManagedSampleAsync(string project, params string[] arguments)
        => DotNetAsync(["run", "--project", project, "-c", Configuration, "--no-build", "--", .. arguments]);

    private Task<string> RunOpenAlSampleAsync(string project, params string[] arguments)
        => DotNetWithEnvironmentAsync(_openAlEnvironment, ["run", "--project", project, "-c", Configuration, "--no-build", "--", .. arguments]);

    private async Task<string> PublishAotSampleAsync(string project, string directory, string executable, bool strict)
    {
        string output = Path.Combine(ArtifactRoot, directory);
        var arguments = new List<string> { "publish", project, "-c", Configuration, "-p:PublishAot=true", "-o", output };
        if (strict)
        {
            arguments.AddRange(["-p:ILLinkTreatWarningsAsErrors=true", "-p:IlcTreatWarningsAsErrors=true"]);
        }

        await DotNetAsync(arguments.ToArray()).ConfigureAwait(false);
        return Path.Combine(output, executable + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
    }

}
