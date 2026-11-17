using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private Target TestUiMedia => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Media.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Media.Parley.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await RunManagedSampleAsync("samples/Lumyte.UI.Media.Sample", "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        });

    private Target TestUiLayout => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await RunManagedSampleAsync("samples/Lumyte.UI.Layout.Sample").ConfigureAwait(false);
        });

    private Target TestUiLayoutAot => definition => definition
        .Executes(async () =>
        {
            string layout = await PublishAotSampleAsync("samples/Lumyte.UI.Layout.Sample", "ui-layout-aot", "Lumyte.UI.Layout.Sample", strict: true).ConfigureAwait(false);
            await ToolAsync(layout).ConfigureAwait(false);
        });

    private Target TestUiScroll => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Controls.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await RunManagedSampleAsync("samples/Lumyte.UI.Scroll.Sample", "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        });

    private Target TestUiScrollAot => definition => definition
        .Executes(async () =>
        {
            string scroll = await PublishAotSampleAsync("samples/Lumyte.UI.Scroll.Sample", "ui-scroll-aot", "Lumyte.UI.Scroll.Sample", strict: true).ConfigureAwait(false);
            await ToolAsync(scroll, "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        });

    private Target TestUiMenus => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Controls.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Windowing.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await RunManagedSampleAsync("samples/Lumyte.UI.Menus.Sample", "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        });

    private Target TestUiMenusAot => definition => definition
        .Executes(async () =>
        {
            string menus = await PublishAotSampleAsync("samples/Lumyte.UI.Menus.Sample", "ui-menus-aot", "Lumyte.UI.Menus.Sample", strict: true).ConfigureAwait(false);
            await ToolAsync(menus, "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        });

    private Target TestUiTabs => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Controls.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await DotNetAsync("test", "tests/UI/Lumyte.UI.TextEditing.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await RunManagedSampleAsync("samples/Lumyte.UI.Tabs.Sample", "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        });

    private Target TestUiTabsAot => definition => definition
        .Executes(async () =>
        {
            string tabs = await PublishAotSampleAsync("samples/Lumyte.UI.Tabs.Sample", "ui-tabs-aot", "Lumyte.UI.Tabs.Sample", strict: true).ConfigureAwait(false);
            await ToolAsync(tabs, "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        });

    private Target TestUiCollections => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Collections.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await RunManagedSampleAsync("samples/Lumyte.UI.Collections.Sample", "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        });

    private Target TestUiCollectionsAot => definition => definition
        .Executes(async () =>
        {
            string collections = await PublishAotSampleAsync("samples/Lumyte.UI.Collections.Sample", "ui-collections-aot", "Lumyte.UI.Collections.Sample", strict: true).ConfigureAwait(false);
            await ToolAsync(collections, "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        });

    private Target TestUiNumeric => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Controls.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await DotNetAsync("test", "tests/UI/Lumyte.UI.TextEditing.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Automation.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await RunManagedSampleAsync("samples/Lumyte.UI.Numeric.Sample", "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        });

    private Target TestUiNumericAot => definition => definition
        .Executes(async () =>
        {
            string numeric = await PublishAotSampleAsync("samples/Lumyte.UI.Numeric.Sample", "ui-numeric-aot", "Lumyte.UI.Numeric.Sample", strict: true).ConfigureAwait(false);
            await ToolAsync(numeric, "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        });

    private Target TestUiControls => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Controls.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await RunManagedSampleAsync("samples/Lumyte.UI.Controls.Sample", "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        });

    private Target TestUiControlsAot => definition => definition
        .Executes(async () =>
        {
            string controls = await PublishAotSampleAsync("samples/Lumyte.UI.Controls.Sample", "ui-controls-aot", "Lumyte.UI.Controls.Sample", strict: true).ConfigureAwait(false);
            await ToolAsync(controls, "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf").ConfigureAwait(false);
        });

    private Target TestUiApplication => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Application.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await DotNetAsync("run", "--project", "samples/Lumyte.UI.Application.Headless.Sample", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
        });

    private Target TestUiNavigation => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Navigation.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await DotNetAsync("run", "--project", "samples/Lumyte.UI.Navigation.Sample", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
        });

    private Target TestUiPages => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", "tests/UI/Lumyte.UI.Application.Navigation.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await DotNetAsync("run", "--project", "samples/Lumyte.UI.Pages.Sample", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await DotNetAsync("run", "--project", "samples/Lumyte.UI.Transitions.Sample", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
        });

}
