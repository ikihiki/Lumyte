using Lumyte.Core.Time;
using Lumyte.Graphics.Text.Parley;
using Lumyte.Input.Abstractions;
using Lumyte.Signals;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Diagnostics;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Lumyte.UI.Controls;
using Lumyte.UI.Dialogs.Sample;
using Lumyte.UI.Media.Parley;
using Lumyte.UI.Menus.Sample;
using Lumyte.UI.TextEditing.Parley;
using Lumyte.UI.Windowing;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;
using static Lumyte.UI.Controls.Windowing.DialogCompose;
using static Lumyte.UI.Media.Compose;
using static Lumyte.UI.TextEditing.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Lumyte.UI.Dialogs.Sample <explicit-font.ttf>");
    return 2;
}

using var engine = new ParleyTextEngine2D();
string family;
using (FileStream font = File.OpenRead(args[0]))
{
    family = engine.AddFont(font);
}

var text = new ParleyTextLayoutProvider(engine);
var editing = new ParleyUiTextLayoutProvider(engine, new() { FontFamily = family });
using var backend = new MenusTextBackend();
var clock = new SampleClock();
var open = Signal.Create(false);
var nativeOpen = Signal.Create(false);
UiApplicationWindowContext? context = null;
ToastController? notifications = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
int requests = 0;
int openedDialogs = 0;
int closedDialogs = 0;
int closedToasts = 0;
int nativeFailures = 0;
UiApplicationBuilder builder = UiApplication.CreateBuilder([]).UseHeadless(new()
{
    ClientSize = new(480, 360),
    TextInputService = backend,
    TextInputTargets = backend,
}).UseTextInput(_ => editing);
builder.UseDiagnostics(new() { Target = new() { AllowInput = true } });
builder.UseWindows(_ => [Window(
    "main",
    "Dialogs and notifications",
    root: value =>
{
    context = value;
    notifications ??= new(clock, 2);
    Widget Label(string label) => Text(value: label, layoutProvider: text, width: 160, height: 24);
    Widget dialogContent = DialogContent()[
        DialogContent.Header()[Label("Details")],
        DialogContent.Content()[UiRecipes.PropertyField(Label("Value"), TextBox(key: "dialog-editor", layoutProvider: editing, automationName: "Value", testId: "dialog-editor", width: 180, height: 32))],
        DialogContent.Actions()[UiRecipes.Toolbar([Button(testId: "save", width: 80, height: 28, onClick: () => open.Value = false)[Label("Save")]])],
        DialogContent.Close()[Rectangle(width: 12, height: 12)]];
    Widget details = Dialog(
        "details",
        "Details",
        isOpen: open,
        initialClientSize: new(260, 220),
        initialFocus: "dialog-editor",
        closeOnOutsidePress: true,
        onCloseRequested: _ =>
        {
            requests++;
            open.Value = false;
        },
        onOpened: _ => openedDialogs++,
        onClosed: _ => closedDialogs++)[dialogContent];
    return Panel()[
        Stack()[
            UiRecipes.FormRow(Label("Name"), TextBox(text: "owner", layoutProvider: editing, automationName: "Name", testId: "owner-editor", width: 180, height: 32)),
            UiRecipes.IconButton(Label("Open"), "Open dialog", () => open.Value = true),
            Button(testId: "open", width: 120, height: 28, onClick: () => open.Value = true)[Label("Details")],
            UiRecipes.Card(UiRecipes.EmptyState(Label("Ready"))),
            UiRecipes.StatusBar([Label("Hosted")])],
        ToastHost(notifications, width: 220, height: 96, with: [Align.At(1, 1)]),
        Dialog("native-proof", "Native capability", isOpen: nativeOpen, presentation: DialogPresentation.NativeRequired, onClosed: NativeClosed)[Label("Not presented")],
        details];
},
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task running = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
var ownerPage = new UiAutomationPage(context!.Automation);
await ownerPage.GetByTestId("owner-editor").FocusAsync();
await Frames();
await app.Dispatcher.InvokeAsync(backend.BeginPreedit);
await app.Dispatcher.InvokeAsync(() => open.Value = true);
await Frames();
IUiAutomationHost dialogHost = await app.Dispatcher.InvokeAsync(() => context.GetDialogAutomation(context.CaptureDialogs().Single().DialogId));
var dialogPage = new UiAutomationPage(dialogHost);
await dialogPage.GetByTestId("dialog-editor").FillAsync("edited");
await app.Dispatcher.InvokeAsync(() =>
{
    if (backend.HasPreedit || openedDialogs != 1 || context.View.CurrentSurface!.FocusedElement is not null)
    {
        throw new InvalidOperationException("Modal opening did not cancel the owner editor exactly once.");
    }
});
await Keys(dialogHost, Key.Tab, Key.Tab);
await Keys(dialogHost, Key.Escape);
await Frames();
await app.Dispatcher.InvokeAsync(() =>
{
    if (requests != 1 || closedDialogs != 1 || context.CaptureDialogs().Count != 0 || context.View.CurrentSurface!.FocusedElement?.Widget.TestId != "owner-editor")
    {
        throw new InvalidOperationException("Controlled closure or after-drain focus restoration failed.");
    }
});
await app.Dispatcher.InvokeAsync(() => nativeOpen.Value = true);
await Frames();
await app.Dispatcher.InvokeAsync(() =>
{
    if (nativeFailures != 1 || context.CaptureDialogs().Count != 0)
    {
        throw new InvalidOperationException("NativeRequired did not fail once before presentation.");
    }

    nativeOpen.Value = false;
});
ToastId second = default;
await app.Dispatcher.InvokeAsync(() =>
{
    ToastId first = default;
    first = notifications!.Enqueue(() => Toast(onDismissRequested: () => notifications.Dismiss(first))[Toast.Content()[Text(value: "First", layoutProvider: text, width: 140, height: 24)]], TimeSpan.FromSeconds(1), _ => closedToasts++);
    second = notifications.Enqueue(() => Toast(onDismissRequested: () => notifications.Dismiss(second), dismissName: "Dismiss second")[Toast.Content()[Text(value: "Second", layoutProvider: text, width: 140, height: 24)]], TimeSpan.Zero, _ => closedToasts++);
    backend.BeginPreedit();
});
await Frames();
await app.Dispatcher.InvokeAsync(() =>
{
    if (!backend.HasPreedit || context.View.CurrentSurface!.FocusedElement?.Widget.TestId != "owner-editor")
    {
        throw new InvalidOperationException("Notification admission stole editing focus or preedit.");
    }

    clock.Now += Duration.FromSeconds(1);
});
await Frames();
await ownerPage.GetByRole(UiRole.Button, "Dismiss second").ClickAsync();
await Frames();
await app.Dispatcher.InvokeAsync(() =>
{
    if (closedToasts != 2 || notifications!.CaptureSnapshot().Count != 0)
    {
        throw new InvalidOperationException("FIFO expiry or ordinary dismissal duplicated callbacks.");
    }

    notifications.Dispose();
});
await app.StopAsync();
await running;
MenusDiagnosticsSmoke.Run();
Console.WriteLine("Hosted controlled dialog, real Parley editing, FIFO notification clock and schema 1-9 AOT metadata passed. Native OS modal activation is not claimed.");
return 0;

void NativeClosed(UiDialogClosed result)
{
    if (result.Reason != UiDialogCloseReason.Failed || result.Code != "native-unavailable" || result.Presentation is not null)
    {
        throw new InvalidOperationException("Unexpected native dialog capability result.");
    }

    nativeFailures++;
}

async Task Frames()
{
    for (int frame = 0; frame < 18; frame++)
    {
        await context.Automation.NextFrameAsync();
    }
}

async Task Keys(IUiAutomationHost host, params Key[] keys)
{
    await using IUiAutomationInputLease lease = await host.AcquireInputAsync();
    await host.RunOnUiThreadAsync(surface =>
    {
        foreach (Key key in keys)
        {
            lease.Dispatch(surface, new UiKeyInput(key, true));
            lease.Dispatch(surface, new UiKeyInput(key, false));
        }

        return true;
    });
}
