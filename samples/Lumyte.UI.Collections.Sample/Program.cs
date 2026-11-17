using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.Graphics.Text.Parley;
using Lumyte.Input.Abstractions;
using Lumyte.UI;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Diagnostics;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Lumyte.UI.Collections;
using Lumyte.UI.Controls.Windowing;
using Lumyte.UI.Media.Parley;
using Lumyte.UI.Windowing;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Media.Compose;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: Lumyte.UI.Collections.Sample <explicit-font.ttf>");
    return 2;
}

using var engine = new ParleyTextEngine2D();
using (FileStream font = File.OpenRead(args[0]))
{
    engine.AddFont(font);
}

var provider = new ParleyTextLayoutProvider(engine);
UiApplicationWindowContext? context = null;
var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
int changes = 0;
string[] items = ["alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta"];
UiApplicationBuilder builder = UiApplication.CreateBuilder([]);
builder.UseHeadless(new() { ClientSize = new(400, 400) });
builder.UseDiagnostics(new() { Target = new() { AllowInput = true } });
builder.UseWindows(_ => [Window(
    "main",
    "Collections",
    root: value =>
{
    context = value;
    var popupHost = new CollectionPopupHost(value.Popups, () => value.PopupOwner);
    return Stack()[
        ItemsRepeater<string>(items: new[] { "header" }, keySelector: item => item, itemTemplate: item => Text(value: item, layoutProvider: provider, height: 24)),
        ListBox<string>(items: items, keySelector: item => item, itemTemplate: item => Text(value: item, layoutProvider: provider, height: 24), selectedKey: "alpha", testId: "list", height: 96),
        ComboBox<string>(items: items, keySelector: item => item, itemTemplate: item => Text(value: item, layoutProvider: provider, height: 24), selectedItemTemplate: item => Text(value: item, layoutProvider: provider, height: 24), emptyTemplate: () => Text(value: "Choose", layoutProvider: provider, height: 24), popupHost: popupHost, popupSize: new System.Numerics.Vector2(180, 120), selectedKey: "alpha", onSelectionChanged: _ => changes++, testId: "combo")];
},
    initialClientSize: new(400, 400),
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task running = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
var page = new UiAutomationPage(context!.Automation);
await page.GetByTestId("list").SelectOptionAsync("beta");
await using (IUiAutomationInputLease listLease = await context.Automation.AcquireInputAsync())
{
    await context.Automation.RunOnUiThreadAsync(surface =>
    {
        UiAutomationNode list = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "list");
        var owner = (ListBoxElement<string>)surface.FindElement(list.Id)!;
        listLease.Dispatch(surface, new UiWheelInput(new(10, 50), new(0, 48)));
        if (((ScrollViewElement)owner.Children[0]).Controller.Offset <= 0)
        {
            throw new InvalidOperationException("List wheel did not use the actual viewport.");
        }

        return true;
    });
}

await page.GetByTestId("combo").ClickAsync();
Guid popupId = Guid.Empty;
for (int frame = 0; frame < 30 && popupId == Guid.Empty; frame++)
{
    await context.Automation.NextFrameAsync();
    await app.Dispatcher.InvokeAsync(() => popupId = context.Popups.CapturePopups().FirstOrDefault(popup => popup.State == UiPopupState.Open)?.Id ?? Guid.Empty);
}

if (popupId == Guid.Empty)
{
    throw new InvalidOperationException("Combo popup failed to open through ordinary input.");
}

IUiAutomationHost popupAutomation = await app.Dispatcher.InvokeAsync(() => context.GetPopupAutomation(popupId));
await using (IUiAutomationInputLease lease = await popupAutomation.AcquireInputAsync())
{
    await popupAutomation.RunOnUiThreadAsync(surface =>
    {
        lease.Dispatch(surface, new UiKeyInput(Key.ArrowDown, true));
        lease.Dispatch(surface, new UiKeyInput(Key.ArrowDown, false));
        lease.Dispatch(surface, new UiKeyInput(Key.Enter, true));
        lease.Dispatch(surface, new UiKeyInput(Key.Enter, false));
        return true;
    });
}

await context.Automation.NextFrameAsync();
await app.Dispatcher.InvokeAsync(() =>
{
    UiSurface surface = context.View.CurrentSurface!;
    UiAutomationNode node = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.TestId == "combo");
    if (((ComboBoxElement<string>)surface.FindElement(node.Id)!).SelectedKey != "beta" || changes != 1)
    {
        throw new InvalidOperationException("Popup did not commit exactly once into its selector owner.");
    }
});
await page.GetByTestId("combo").ClickAsync();
for (int frame = 0; frame < 15; frame++)
{
    await context.Automation.NextFrameAsync();
}

Guid cancelPopup = await app.Dispatcher.InvokeAsync(() => context.Popups.CapturePopups().Single(popup => popup.State == UiPopupState.Open).Id);
IUiAutomationHost cancelHost = await app.Dispatcher.InvokeAsync(() => context.GetPopupAutomation(cancelPopup));
await using (IUiAutomationInputLease cancelLease = await cancelHost.AcquireInputAsync())
{
    await cancelHost.RunOnUiThreadAsync(surface =>
    {
        cancelLease.Dispatch(surface, new UiKeyInput(Key.Escape, true));
        cancelLease.Dispatch(surface, new UiKeyInput(Key.Escape, false));
        return true;
    });
}

for (int frame = 0; frame < 15; frame++)
{
    await context.Automation.NextFrameAsync();
}

await page.GetByTestId("combo").ClickAsync();
for (int frame = 0; frame < 15; frame++)
{
    await context.Automation.NextFrameAsync();
}

await app.Dispatcher.InvokeAsync(() => context.Popups.NotifyOutsidePress(context.PopupOwner));
for (int frame = 0; frame < 15; frame++)
{
    await context.Automation.NextFrameAsync();
}

await app.Dispatcher.InvokeAsync(() =>
{
    UiSurface surface = context.View.CurrentSurface!;
    surface.Rebuild(ListBox<string>(items: new[] { "gamma", "beta", "delta" }, keySelector: item => item, itemTemplate: item => Text(value: item, layoutProvider: provider, height: 24), selectedKey: "beta", testId: "reset-list"));
    surface.UpdateLayout();
    if (((ListBoxElement<string>)surface.RootElement).SelectedKey != "beta")
    {
        throw new InvalidOperationException("Typed item reset did not adopt its ordinal selection.");
    }
});

await app.Dispatcher.InvokeAsync(() =>
{
    UiRemotePlan plan = UiRemoteSchema.ParsePlan("{\"schemaVersion\":7,\"kind\":\"count\",\"locator\":[{\"kind\":\"role\",\"role\":\"listbox\"}]}");
    byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(plan, UiAutomationJsonContext.Default.UiRemotePlan);
    if (UiRemoteSchema.ParsePlan(System.Text.Encoding.UTF8.GetString(serialized)).SchemaVersion != 7)
    {
        throw new InvalidOperationException("AOT schema metadata failed.");
    }
});

if (changes != 1)
{
    throw new InvalidOperationException("Cancellation must not emit selection callbacks.");
}

await app.StopAsync();
await running;
Console.WriteLine("C7 typed repeater/list/combo with explicit Parley and normal popup input passed.");
return 0;
