using Lumyte.Graphics.Text.Parley;
using Lumyte.TextEditing;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Lumyte.UI.Controls;
using Lumyte.UI.Tabs.Sample;
using Lumyte.UI.TextEditing;
using Lumyte.UI.TextEditing.Parley;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;
using static Lumyte.UI.TextEditing.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Lumyte.UI.Tabs.Sample <explicit-font.ttf>");
    return 2;
}

using var engine = new ParleyTextEngine2D();
string family;
using (FileStream font = File.OpenRead(args[0]))
{
    family = engine.AddFont(font);
}

var provider = new ParleyUiTextLayoutProvider(engine, new() { FontFamily = family });
using var backend = new TabsTextBackend();
UiApplicationWindowContext? context = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
int selectionChanges = 0;
int expansionChanges = 0;
UiApplicationBuilder builder = UiApplication.CreateBuilder([]).UseHeadless(new()
{
    ClientSize = new(400, 320),
    TextInputService = backend,
    TextInputTargets = backend,
}).UseTextInput(_ => provider);
builder.UseWindows(_ => [Window(
    "main",
    "Retained tabs",
    root: value =>
{
    context = value;
    Widget editorContent = TextBox(text: "retained", layoutProvider: provider, testId: "editor", width: 180, height: 40);
    Widget details = Expander(isExpanded: true, testId: "details", onChanged: _ => expansionChanges++)[Expander.Header()[Rectangle(width: 160, height: 24)], Expander.Content()[editorContent]];
    Widget editTab = TabItem(key: "edit", testId: "edit-tab")[TabItem.Header()[Rectangle(width: 120, height: 24)], TabItem.Content()[details]];
    Widget otherTab = TabItem(key: "other", testId: "other-tab")[TabItem.Header()[Rectangle(width: 120, height: 24)], TabItem.Content()[Rectangle(width: 180, height: 60)]];
    return TabView(selectedKey: "edit", testId: "tabs", onSelectionChanged: _ => selectionChanges++)[editTab, otherTab];
},
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task run = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
var page = new UiAutomationPage(context!.Automation);
await page.GetByTestId("editor").FillAsync("preserved edit");
TextBoxElement editor = await context.Automation.RunOnUiThreadAsync(surface => (TextBoxElement)surface.FindElement(surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "editor").Id)!);
TextDocument originalDocument = await context.Automation.RunOnUiThreadAsync(_ => editor.Document);
await page.GetByTestId("editor").FocusAsync();
await context.Automation.NextFrameAsync();
await using (IUiAutomationInputLease lease = await context.Automation.AcquireInputAsync())
{
    await context.Automation.RunOnUiThreadAsync(surface =>
    {
        backend.BeginPreedit();
        UiAutomationNode other = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "other-tab");
        var point = new System.Numerics.Vector2(other.ActionableBounds.X + 2, other.ActionableBounds.Y + 2);
        lease.Dispatch(surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Down, point));
        lease.Dispatch(surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Up, point));
        return true;
    });
}

await context.Automation.NextFrameAsync();
await context.Automation.NextFrameAsync();
await page.Expect(page.GetByTestId("editor")).ToBeHiddenAsync();
if (backend.HasPreedit || selectionChanges != 1)
{
    throw new InvalidOperationException("Actual retained editor preedit survived tab deactivation.");
}

await page.GetByTestId("tabs").SelectOptionAsync("edit");
await page.GetByTestId("details").CollapseAsync();
await page.GetByTestId("details").CollapseAsync();
await page.Expect(page.GetByTestId("editor")).ToBeHiddenAsync();
await page.GetByTestId("details").ExpandAsync();
UiAutomationSnapshot snapshot = await page.SnapshotAsync();
if (snapshot.Nodes.Single(node => node.TestId == "details").IsExpanded != true || expansionChanges != 2 || selectionChanges != 2)
{
    throw new InvalidOperationException("Accepted expansion or selection did not match ordinary input.");
}

await app.Dispatcher.InvokeAsync(() =>
{
    UiSurface surface = context.View.CurrentSurface!;
    UiAutomationNode editorNode = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "editor");
    if (!ReferenceEquals(editor, surface.FindElement(editorNode.Id)) || !ReferenceEquals(originalDocument, editor.Document)
        || editor.Document.CaptureSnapshot().CommittedText != "preserved edit")
    {
        throw new InvalidOperationException("Retained editor identity changed.");
    }
});
await app.StopAsync();
await run;
TabsDiagnosticsSmoke.Run();
Console.WriteLine("C8 retained TabView/TabItem/Expander with real Parley editing and ordinary leases passed.");
return 0;
