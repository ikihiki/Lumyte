using System.Numerics;
using Lumyte.Graphics.Text.Parley;
using Lumyte.Input.Abstractions;
using Lumyte.Signals;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Lumyte.UI.Controls.Windowing;
using Lumyte.UI.Media.Parley;
using Lumyte.UI.Menus.Sample;
using Lumyte.UI.TextEditing.Parley;
using Lumyte.UI.Windowing;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;
using static Lumyte.UI.Media.Compose;
using static Lumyte.UI.TextEditing.Compose;
using static Lumyte.UI.Windowing.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Lumyte.UI.Menus.Sample <explicit-font.ttf>");
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
var menuOpen = Signal.Create(false);
var tipOpen = Signal.Create(false);
var checkedState = Signal.Create(false);
int changes = 0;
int commands = 0;
int submenus = 0;
int tooltips = 0;
int anchorClicks = 0;
UiApplicationWindowContext? context = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
UiApplicationBuilder builder = UiApplication.CreateBuilder([]).UseHeadless(new()
{
    ClientSize = new(480, 400),
    TextInputService = backend,
    TextInputTargets = backend,
}).UseTextInput(_ => editing);
builder.UseWindows(_ => [Window(
    "main",
    "Menus and passive tooltip",
    root: value =>
{
    context = value;
    var host = new MenuPopupHost(value.Popups, () => value.PopupOwner);
    Widget Label(string label) => Text(value: label, layoutProvider: text, width: 160, height: 28);
    Widget Submenu()
    {
        submenus++;
        return Menu(popupHost: host)[MenuItem(key: "execute", testId: "execute", onInvoke: () => commands++)[Label("Execute")]];
    }

    Widget CreateHelp()
    {
        tooltips++;
        return Text(value: "Passive help; clicks pass through", layoutProvider: text, width: 200, height: 80);
    }

    void ChangeChecked(bool nextChecked)
    {
        checkedState.Value = nextChecked;
        changes++;
    }

    Widget menu = Menu(popupHost: host, testId: "menu-content")[
        MenuItem(key: "save", testId: "save", onInvoke: () => commands++)[Label("Save")],
        MenuItem(key: "check", testId: "check", isCheckable: true, isChecked: checkedState, onChanged: ChangeChecked)[Label("Check command")],
        Separator(),
        MenuItem(key: "sub", testId: "sub", createSubmenu: Submenu, popupSize: new Vector2(180, 80))[Label("More")]];
    Widget portal = Popup(key: "menu", anchor: "open", isOpen: menuOpen, onDismiss: _ => menuOpen.Value = false)[menu];
    Widget tooltip = Tooltip(testId: "tip-anchor", popupHost: host, isOpen: tipOpen, popupSize: new Vector2(200, 80), createPopup: CreateHelp)[Button(testId: "behind", onClick: () => anchorClicks++)[Label("Ordinary anchor")]];
    return Stack()[
        Button(key: "open", testId: "open", onClick: () => menuOpen.Value = true)[Label("Open menu")],
        TextBox(text: "retained editor", layoutProvider: editing, testId: "editor", width: 180, height: 40),
        tooltip,
        portal];
},
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task running = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
var page = new UiAutomationPage(context!.Automation);

// Explicit state changes are app-model updates, never mutations of mounted Widgets.
await page.GetByTestId("editor").FocusAsync();
await context.Automation.NextFrameAsync();
await app.Dispatcher.InvokeAsync(() =>
{
    backend.BeginPreedit();
    tipOpen.Value = true;
});
await Frames();
await app.Dispatcher.InvokeAsync(() =>
{
    if (!backend.HasPreedit || context.View.CurrentSurface!.FocusedElement?.Widget.TestId != "editor"
        || context.Popups.CapturePopups().Count(p => p.State == UiPopupState.Open && p.InputMode == UiPopupInputMode.Passive) != 1 || tooltips != 1)
    {
        throw new InvalidOperationException("Passive tooltip changed editor focus/preedit or failed to open once.");
    }
});
await app.Dispatcher.InvokeAsync(() => tipOpen.Value = false);
await Frames();
await app.Dispatcher.InvokeAsync(() =>
{
    if (!backend.HasPreedit || context.View.CurrentSurface!.FocusedElement?.Widget.TestId != "editor")
    {
        throw new InvalidOperationException("Tooltip closing changed editor focus/preedit.");
    }
});
await app.Dispatcher.InvokeAsync(() => tipOpen.Value = true);
await Frames();
await page.GetByTestId("behind").ClickAsync();
if (anchorClicks != 1)
{
    throw new InvalidOperationException("Passive content consumed ordinary anchor input.");
}

await app.Dispatcher.InvokeAsync(() => tipOpen.Value = false);
await page.GetByTestId("open").ClickAsync();
IUiAutomationHost rootHost = await MenuHost();
await Keys(rootHost, Key.ArrowDown, Key.Enter);
await Frames();
if (changes != 1 || !checkedState.Peek())
{
    throw new InvalidOperationException("Check command was not accepted exactly once through normal input.");
}

await page.GetByTestId("open").ClickAsync();
rootHost = await MenuHost();
await Keys(rootHost, Key.End);
var menuPage = new UiAutomationPage(rootHost);
await menuPage.GetByTestId("sub").ExpandAsync();
await menuPage.GetByTestId("sub").ExpandAsync();
await Frames();
Guid child = await app.Dispatcher.InvokeAsync(() => context.Popups.CapturePopups().Single(p => p.State == UiPopupState.Open && p.ParentPopupId is not null).Id);
IUiAutomationHost childHost = await app.Dispatcher.InvokeAsync(() => context.GetPopupAutomation(child));
await Keys(childHost, Key.Enter);
await Frames();
if (commands != 1 || submenus != 1)
{
    throw new InvalidOperationException("Submenu factory/command was duplicated or failed to execute.");
}

await app.Dispatcher.InvokeAsync(() =>
{
    if (context.Popups.CapturePopups().Any(p => p.InputMode == UiPopupInputMode.Menu) || context.View.CurrentSurface!.FocusedElement?.Widget.TestId != "open")
    {
        throw new InvalidOperationException("Chain close or owner focus restoration failed.");
    }
});
await page.GetByTestId("open").ClickAsync();
await Keys(await MenuHost(), Key.Escape);
await Frames();
await page.GetByTestId("open").ClickAsync();
await MenuHost();
await app.Dispatcher.InvokeAsync(() => context.Popups.NotifyOutsidePress(context.PopupOwner));
await Frames();
await app.Dispatcher.InvokeAsync(() =>
{
    if (context.Popups.CapturePopups().Count != 0)
    {
        throw new InvalidOperationException("Escape/outside cancellation left ghost popups.");
    }
});
MenusDiagnosticsSmoke.Run();
await app.StopAsync();
await running;
Console.WriteLine("C8 menu/check/submenu/chain cleanup and explicit passive tooltip with preserved editor preedit passed.");
return 0;

async Task Frames()
{
    for (int frame = 0; frame < 15; frame++)
    {
        await context.Automation.NextFrameAsync();
    }
}

async Task<IUiAutomationHost> MenuHost()
{
    await Frames();
    Guid id = await app.Dispatcher.InvokeAsync(() => context.Popups.CapturePopups().Single(p => p.State == UiPopupState.Open && p.InputMode == UiPopupInputMode.Menu && p.ParentPopupId is null).Id);
    return await app.Dispatcher.InvokeAsync(() => context.GetPopupAutomation(id));
}

async Task Keys(IUiAutomationHost host, params Key[] keys)
{
    await using IUiAutomationInputLease lease = await host.AcquireInputAsync();
    await host.RunOnUiThreadAsync(surface =>
    {
        foreach (Key key in keys)
        {
            lease.Dispatch(surface, new UiKeyInput(key, true));
            lease.Dispatch(surface, new UiKeyInput(key, true, IsRepeat: true));
            lease.Dispatch(surface, new UiKeyInput(key, false));
        }

        return true;
    });
}
