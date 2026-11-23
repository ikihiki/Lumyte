using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.Graphics.Text.Parley;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Diagnostics;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Lumyte.UI.Collections;
using Lumyte.UI.Media.Parley;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Media.Compose;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Lumyte.UI.TreeView.Sample <explicit-font.ttf>");
    return 2;
}

var wire = new UiRemotePlan(11, "count", [new("role", Role: "tree")]);
byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(wire, UiAutomationJsonContext.Default.UiRemotePlan);
if (UiRemoteSchema.ParsePlan(System.Text.Encoding.UTF8.GetString(encoded)).SchemaVersion != 11)
{
    throw new InvalidOperationException("The closed schema-eleven source-generated metadata failed.");
}

using var engine = new ParleyTextEngine2D();
string family;
using (FileStream font = File.OpenRead(args[0]))
{
    family = engine.AddFont(font);
}

var provider = new ParleyTextLayoutProvider(engine, new() { FontFamily = family });
UiApplicationWindowContext? context = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
int templates = 0;
UiApplicationBuilder builder = UiApplication.CreateBuilder([]);
builder.UseHeadless(new() { ClientSize = new(400, 240) });
builder.UseDiagnostics(new() { Target = new() { AllowInput = true } });
builder.UseWindows(_ => [Window(
    "main",
    "TreeView",
    root: value =>
    {
        context = value;
        Lumyte.UI.Compose.Definitions.Widget Render(string key, Lumyte.UI.Collections.Compose.Definitions.TreeView<string> owner)
        {
            templates++;
            return Text(value: key, layoutProvider: provider, height: 24);
        }

        return TreeView<string>(
            roots: new[] { "root" },
            keySelector: key => key,
            hasChildren: key => key == "root",
            childrenSelector: _ => Enumerable.Range(0, 1000).Select(index => "child" + index).ToArray(),
            keyPathSelector: key => key == "root" ? new[] { "root" } : new[] { "root", key },
            virtualization: new UiItemVirtualization(24, 2),
            testId: "tree",
            height: 240)[
            TreeViewFactory<string>().Item()[Render]];
    },
    initialClientSize: new(400, 240),
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task running = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
var page = new UiAutomationPage(context!.Automation);
await page.GetByTestId("tree").GetByItemKey("child999").BringIntoViewAsync();
await page.GetByTestId("tree").SelectOptionAsync("child999");
await context.Automation.RunOnUiThreadAsync(surface =>
{
    var tree = (TreeViewElement<string>)surface.RootElement;
    UiAutomationNode child = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.OptionKey == "child999");
    if (tree.SelectedKey != "child999" || child.TreeItem != new UiTreeItemState(1, "root") || templates > 40)
    {
        throw new InvalidOperationException("Tree path reveal, hierarchy metadata or bounded realization failed.");
    }

    tree.SetExpanded("root", false);
    surface.UpdateLayout();
    if (tree.VisibleKeys.Count != 1 || tree.SelectedKey != "child999")
    {
        throw new InvalidOperationException("Collapse did not retain logical selection.");
    }

    return true;
});
await app.StopAsync();
await running;
Console.WriteLine("Lazy TreeView path reveal and bounded selection passed.");
return 0;
