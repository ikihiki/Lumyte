namespace Lumyte.Build;

internal sealed partial class Build
{
    private sealed record UiScenario(string[] Projects, string[] Samples, bool Font, bool SupportsAot);

    private static readonly IReadOnlyDictionary<string, UiScenario> _uiScenarios = new Dictionary<string, UiScenario>(StringComparer.Ordinal)
    {
        ["webview"] = new(["tests/UI/Lumyte.UI.WebView.Tests"], ["samples/Lumyte.UI.WebView.Helper.Sample", "samples/Lumyte.UI.WebView.Sample"], false, false),
        ["media"] = new(["tests/UI/Lumyte.UI.Media.Tests", "tests/UI/Lumyte.UI.Media.Parley.Tests"], ["samples/Lumyte.UI.Media.Sample"], true, false),
        ["layout"] = new(["tests/UI/Lumyte.UI.Tests"], ["samples/Lumyte.UI.Layout.Sample"], false, true),
        ["scroll"] = new(["tests/UI/Lumyte.UI.Tests", "tests/UI/Lumyte.UI.Controls.Tests"], ["samples/Lumyte.UI.Scroll.Sample"], true, true),
        ["menus"] = new(["tests/UI/Lumyte.UI.Controls.Tests", "tests/UI/Lumyte.UI.Windowing.Tests"], ["samples/Lumyte.UI.Menus.Sample"], true, true),
        ["dialogs"] = new(["tests/UI/Lumyte.UI.Controls.Tests", "tests/UI/Lumyte.UI.Windowing.Tests", "tests/UI/Lumyte.UI.Application.Tests"], ["samples/Lumyte.UI.Dialogs.Sample"], true, true),
        ["tabs"] = new(["tests/UI/Lumyte.UI.Controls.Tests", "tests/UI/Lumyte.UI.TextEditing.Tests"], ["samples/Lumyte.UI.Tabs.Sample"], true, true),
        ["collections"] = new(["tests/UI/Lumyte.UI.Collections.Tests"], ["samples/Lumyte.UI.Collections.Sample"], true, true),
        ["virtualization"] = new(["tests/UI/Lumyte.UI.Collections.Tests", "tests/UI/Lumyte.UI.Automation.Tests", "tests/UI/Lumyte.UI.Windowing.Tests", "tests/UI/Lumyte.UI.TextEditing.Tests", "tests/Diagnostics/Lumyte.Diagnostics.UI.Automation.Tests"], ["samples/Lumyte.UI.Virtualization.Sample"], true, true),
        ["treeview"] = new(["tests/UI/Lumyte.UI.Collections.Tests", "tests/UI/Lumyte.UI.Automation.Tests", "tests/UI/Lumyte.UI.Windowing.Tests", "tests/UI/Lumyte.UI.TextEditing.Tests", "tests/Diagnostics/Lumyte.Diagnostics.UI.Automation.Tests"], ["samples/Lumyte.UI.TreeView.Sample"], true, true),
        ["numeric"] = new(["tests/UI/Lumyte.UI.Controls.Tests", "tests/UI/Lumyte.UI.TextEditing.Tests", "tests/UI/Lumyte.UI.Automation.Tests"], ["samples/Lumyte.UI.Numeric.Sample"], true, true),
        ["controls"] = new(["tests/UI/Lumyte.UI.Controls.Tests"], ["samples/Lumyte.UI.Controls.Sample"], true, true),
        ["application"] = new(["tests/UI/Lumyte.UI.Application.Tests"], ["samples/Lumyte.UI.Application.Headless.Sample"], false, false),
        ["navigation"] = new(["tests/UI/Lumyte.UI.Navigation.Tests"], ["samples/Lumyte.UI.Navigation.Sample"], false, false),
        ["pages"] = new(["tests/UI/Lumyte.UI.Application.Navigation.Tests"], ["samples/Lumyte.UI.Pages.Sample", "samples/Lumyte.UI.Transitions.Sample"], false, false),
    };

    private void ValidateUiFeature()
    {
        if (Feature is null || !_uiScenarios.TryGetValue(Feature, out UiScenario? scenario))
        {
            throw new ArgumentException("--suite ui requires a valid --feature: " + string.Join(", ", _uiScenarios.Keys));
        }

        if (Aot && !scenario.SupportsAot)
        {
            throw new ArgumentException("The selected UI feature does not have an AOT scenario.");
        }
    }

    private async Task RunUiFeatureAsync()
    {
        UiScenario scenario = _uiScenarios[Feature!];
        string[] arguments = Feature == "webview" ? ["--selfcheck"] : scenario.Font ? ["tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf"] : [];
        if (!Aot)
        {
            foreach (string project in scenario.Projects)
            {
                await DotNetAsync("test", project, "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            }
        }

        foreach (string sample in scenario.Samples)
        {
            if (Aot)
            {
                string executable = await PublishAotSampleAsync(sample, "ui-" + Feature + "-aot", Path.GetFileName(sample), strict: true).ConfigureAwait(false);
                await ToolAsync(executable, arguments).ConfigureAwait(false);
            }
            else
            {
                await RunManagedSampleAsync(sample, arguments).ConfigureAwait(false);
            }
        }
    }
}
