using Lumyte.Graphics.Text.Parley;
using Lumyte.Input.Abstractions;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.TextEditing.Parley;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Lumyte.UI.Scroll.Sample <explicit-font.ttf>");
    return 2;
}

using var engine = new ParleyTextEngine2D();
string family;
using (FileStream font = File.OpenRead(args[0]))
{
    family = engine.AddFont(font);
}

var provider = new ParleyUiTextLayoutProvider(engine, new() { FontFamily = family });
using var outer = new ScrollController();
using var inner = new ScrollController();
UiApplicationWindowContext? context = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
UiApplicationBuilder builder = UiApplication.CreateBuilder([]).UseHeadless().UseTextInput(_ => provider);
builder.UseWindows(_ => [Window(
    "main",
    "Scroll",
    root: value =>
{
    context = value;
    return Grid(columns: new UiGridTrack[] { new(1, true), new(20) })[
        ScrollView(controller: outer, with: [Grid.Cell()])[Stack()[
            ScrollView(controller: inner, height: 80)[SizedBox(height: 100)],
            SizedBox(height: 300),
            new Lumyte.UI.TextEditing.Compose.Definitions.TextBox { Text = "IME", IsReadOnly = true, LayoutProvider = provider, Width = 100, Height = 30, TestId = "editor" },
            Button(width: 100, height: 30, testId: "last")]],
        ScrollBar(controller: outer, testId: "scrollbar", with: [Grid.Cell(column: 1)])];
},
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task run = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(10));
await app.Dispatcher.InvokeAsync(() =>
{
    UiSurface surface = context!.View.CurrentSurface!;
    surface.SetViewport(new(200, 100));
    surface.UpdateLayout();
    surface.Dispatch(new UiWheelInput(new(10), new(0, 50)));
    if (inner.Offset != 20 || outer.Offset != 30)
    {
        throw new InvalidOperationException("Nested wheel residual diverged.");
    }

    outer.ScrollTo(0);
    inner.ScrollTo(0);
    surface.UpdateLayout();
    surface.Dispatch(new UiPointerInput(3, UiPointerPhase.Down, new(10, 60)) { Device = UiPointerDevice.Touch });
    surface.Dispatch(new UiPointerInput(3, UiPointerPhase.Move, new(10, 10)) { Device = UiPointerDevice.Touch });
    surface.Dispatch(new UiPointerInput(3, UiPointerPhase.Up, new(10, 10)) { Device = UiPointerDevice.Touch });
    outer.ScrollTo(0);
    surface.UpdateLayout();
    int changes = 0;
    outer.OnChanged = _ => changes++;
    surface.Dispatch(new UiPointerInput(7, UiPointerPhase.Down, new(190, 5)));
    surface.Dispatch(new UiPointerInput(7, UiPointerPhase.Move, new(190, 35)));
    surface.Dispatch(new UiPointerInput(7, UiPointerPhase.Up, new(190, 35)));
    if (outer.Offset <= 0 || changes != 1)
    {
        throw new InvalidOperationException("Real scrollbar thumb input did not change the borrowed controller exactly once.");
    }

    float dragged = outer.Offset;
    surface.Dispatch(new UiKeyInput(Key.PageDown, true));
    surface.Dispatch(new UiKeyInput(Key.PageDown, false));
    if (outer.Offset <= dragged || changes != 2)
    {
        throw new InvalidOperationException("Real scrollbar keyboard input did not change the controller.");
    }

    outer.ScrollTo(0);
    surface.UpdateLayout();
    surface.Dispatch(new UiPointerInput(8, UiPointerPhase.Down, new(190, 90)));
    surface.Dispatch(new UiPointerInput(8, UiPointerPhase.Up, new(190, 90)));
    if (outer.Offset != outer.Viewport)
    {
        throw new InvalidOperationException("Real scrollbar track input did not page.");
    }

    outer.OnChanged = null;
    UiAutomationNode editorNode = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "editor");
    var editor = (Lumyte.UI.TextEditing.TextBoxElement)surface.FindElement(editorNode.Id)!;
    surface.BringIntoView(editor, editor.ContentBounds);
    surface.TryFocus(editor);
    if (editor.CurrentLayout is null || !ReferenceEquals(surface.FocusedElement, editor))
    {
        throw new InvalidOperationException("The borrowed TextBox provider was not retained through scrolling.");
    }

    UiAutomationNode target = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "last");
    surface.BringIntoView(surface.FindElement(target.Id)!, new(0, 0, 100, 30));
    if (outer.Offset != outer.Maximum)
    {
        throw new InvalidOperationException("BringIntoView did not reveal the last button.");
    }
});
await app.StopAsync();
await run;
Console.WriteLine("C5 headless nested wheel, touch pan, BringIntoView and real ScrollBar passed.");
return 0;
