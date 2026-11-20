using System.Text.Json;
using Lumyte.Composition;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.Graphics.Text.Parley;
using Lumyte.Input.Abstractions;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Diagnostics;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Lumyte.UI.Collections;
using Lumyte.UI.SplitView.Sample;
using Lumyte.UI.TextEditing;
using Lumyte.UI.TextEditing.Parley;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;
using static Lumyte.UI.TextEditing.Compose;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Lumyte.UI.SplitView.Sample <explicit-font.ttf>");
    return 2;
}

var plan = new UiRemotePlan(13, "set-value", [new("role", Role: "splitview"), new("role", Role: "separator")], NumericValue: 0.6);
string json = JsonSerializer.Serialize(plan, UiAutomationJsonContext.Default.UiRemotePlan);
if (UiRemoteSchema.ParsePlan(json).SchemaVersion != 13)
{
    throw new InvalidOperationException("Generated schema thirteen metadata is unavailable.");
}

using var engine = new ParleyTextEngine2D();
string family;
using (FileStream font = File.OpenRead(args[0]))
{
    family = engine.AddFont(font);
}

var provider = new ParleyUiTextLayoutProvider(engine, new() { FontFamily = family });
using var backend = new SplitTextBackend();
UiApplicationWindowContext? context = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
int changed = 0;
int committed = 0;
UiApplicationBuilder builder = UiApplication.CreateBuilder([]).UseHeadless(new()
{
    ClientSize = new(720, 320),
    TextInputService = backend,
    TextInputTargets = backend,
}).UseTextInput(_ => provider);
builder.UseDiagnostics(new() { Target = new() { AllowInput = true } });
builder.UseWindows(_ => [Window(
    "main",
    "SplitView",
    root: value =>
{
    context = value;
    CompositionSlotAssignment<Lumyte.UI.Collections.Compose.Definitions.DataGrid<int>> header = DataGridFactory<int>().Header()[
        (column, owner) => SizedBox()];
    CompositionSlotAssignment<Lumyte.UI.Collections.Compose.Definitions.DataGrid<int>> cell = DataGridFactory<int>().Cell()[
        (value, owner) => SizedBox()];
    Lumyte.UI.Compose.Definitions.Widget grid = DataGrid<int>(with: [SplitView.Pane(minimum: 100)], items: Enumerable.Range(0, 100).ToArray(), keySelector: row => row.ToString(System.Globalization.CultureInfo.InvariantCulture), columns: new DataGridColumn<int>[] { new("value", "Value", 140) })[header, cell];
    return SplitView(testId: "split", onChanged: _ => changed++, onCommitted: _ => committed++)[
        grid,
        TextBox(with: [SplitView.Pane(minimum: 120)], text: "edit", layoutProvider: provider, testId: "editor"),
        SplitView(with: [SplitView.Pane(minimum: 60)], orientation: UiOrientation.Vertical, testId: "nested")[SizedBox(), SizedBox()]];
},
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task running = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
var page = new UiAutomationPage(context!.Automation);
using (var ready = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
{
    while (!await context.Automation.RunOnUiThreadAsync(_ => context.Windows.TryGetSnapshot(context.Window, out Lumyte.UI.Windowing.UiWindowSnapshot? window) && window is { IsVisible: true }, ready.Token))
    {
        await context.Automation.NextFrameAsync(ready.Token);
    }
}

await using (IUiAutomationInputLease lease = await context.Automation.AcquireInputAsync())
{
    await context.Automation.RunOnUiThreadAsync(surface =>
    {
        UiAutomationNode editor = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "editor");
        var point = new System.Numerics.Vector2(editor.ActionableBounds.X + 10, editor.ActionableBounds.Y + 10);
        if (!lease.Dispatch(surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Down, point)))
        {
            throw new InvalidOperationException("The actual editor pointer route was rejected.");
        }

        lease.Dispatch(surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Up, point));
        if (surface.FocusedElement?.Widget.TestId != "editor")
        {
            throw new InvalidOperationException("The actual editor pointer did not acquire focus.");
        }

        return true;
    });
}

await page.GetByTestId("editor").FillAsync("preserved editing");
UiAutomationSnapshot initial = await page.SnapshotAsync();
UiAutomationNode divider = initial.Nodes.First(node => node.Role == UiRole.Separator && node.ParentId == initial.Nodes.Single(parent => parent.TestId == "split").Id);
UiLocator boundary = page.GetByTestId("split.divider.0");
await boundary.SetValueAsync(0.6);
await boundary.PressAsync(Key.ArrowRight);
await context.Automation.NextFrameAsync();
await using (IUiAutomationInputLease lease = await context.Automation.AcquireInputAsync())
{
    await context.Automation.RunOnUiThreadAsync(surface =>
    {
        UiAutomationNode current = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.Id == divider.Id);
        var point = new System.Numerics.Vector2(current.ActionableBounds.X + (current.ActionableBounds.Width / 2), current.ActionableBounds.Y + 20);
        lease.Dispatch(surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Down, point));
        lease.Dispatch(surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Move, point + new System.Numerics.Vector2(12, 0)));
        lease.Dispatch(surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Cancel, point));
        return true;
    });
}

await context.Automation.NextFrameAsync();
await context.Automation.RunOnUiThreadAsync(surface =>
{
    UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
    UiAutomationNode editorNode = snapshot.Nodes.Single(node => node.TestId == "editor");
    var editor = (TextBoxElement)surface.FindElement(editorNode.Id)!;
    if (editor.Document.CaptureSnapshot().CommittedText != "preserved editing" || changed < 2 || committed != 1
        || snapshot.Nodes.Count(node => node.Role == UiRole.Separator) != 3)
    {
        throw new InvalidOperationException("Real divider input did not preserve nested panes and editing state.");
    }

    return true;
});
await app.StopAsync();
await running;
Console.WriteLine("SplitView finite three-pane/nested layout, leased numeric/pointer/keyboard resize and retained Parley editing passed.");
return 0;
