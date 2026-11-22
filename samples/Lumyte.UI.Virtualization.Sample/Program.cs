using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Client;
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

if (args.Length is not (1 or 5))
{
    Console.Error.WriteLine("Usage: Lumyte.UI.Virtualization.Sample <explicit-font.ttf> [--remote base-url session-id target-id]");
    return 2;
}

if (args.Length == 5)
{
    if (args[1] != "--remote")
    {
        return 2;
    }

    using var http = new HttpClient { BaseAddress = new Uri(args[2]) };
    await using var remote = new UiAutomationDiagnosticsClient(http, Guid.Parse(args[3]));
    var targetId = Guid.Parse(args[4]);
    if (!(await remote.ListTargetsAsync()).Any(target => target.Id == targetId))
    {
        throw new InvalidOperationException("The requested diagnostic target is unavailable.");
    }

    await using UiRemoteControl control = await remote.AcquireControlAsync(targetId);
    await control.Page.GetByTestId("list").GetByItemKey("9999").BringIntoViewAsync();
    await control.Page.GetByTestId("list").SelectOptionAsync("9999");
    Console.WriteLine("Typed HTTP key reveal and selection passed.");
    return 0;
}

var wire = new UiRemotePlan(10, "bring-item", [new("test-id", Value: "list"), new("item-key", Value: "9999")]);
byte[] encoded = JsonSerializer.SerializeToUtf8Bytes(wire, UiAutomationJsonContext.Default.UiRemotePlan);
if (UiRemoteSchema.ParsePlan(System.Text.Encoding.UTF8.GetString(encoded)).SchemaVersion != 10)
{
    throw new InvalidOperationException("The source-generated schema-ten runtime metadata failed.");
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
string? selected = null;
string[] items = Enumerable.Range(0, 10000).Select(index => index.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();
UiApplicationBuilder builder = UiApplication.CreateBuilder([]);
builder.UseHeadless(new() { ClientSize = new(400, 240) });
builder.UseDiagnostics(new() { Target = new() { AllowInput = true } });
Lumyte.UI.Collections.Compose.Definitions.ListBox<string> CreateList(string[] model) => ListBox<string>(
    items: model,
    keySelector: item => item,
    itemTemplate: item =>
    {
        templates++;
        return Text(value: item, layoutProvider: provider, height: 24);
    },
    selectedKey: selected,
    onSelectionChanged: key => selected = key,
    virtualization: new UiItemVirtualization(24, 2),
    testId: "list",
    height: 240);
builder.UseWindows(_ => [Window(
    "main",
    "Virtualization",
    root: value =>
    {
        context = value;
        return CreateList(items);
    },
    initialClientSize: new(400, 240),
    onOpened: _ => opened.TrySetResult())]);
await using UiApplication app = builder.Build();
Task running = app.RunAsync();
await opened.Task.WaitAsync(TimeSpan.FromSeconds(15));
var page = new UiAutomationPage(context!.Automation);
await page.GetByTestId("list").GetByItemKey("9999").BringIntoViewAsync();
await page.GetByTestId("list").SelectOptionAsync("9999");
await context.Automation.RunOnUiThreadAsync(surface =>
{
    var list = (ListBoxElement<string>)surface.RootElement;
    if (list.SelectedKey != "9999" || templates > 30 || list.Children[0].Children[0].Children.Count > 14)
    {
        throw new InvalidOperationException("Offscreen key selection failed bounded realization.");
    }

    return true;
});
UiAutomationId evicted = await context.Automation.RunOnUiThreadAsync(surface => surface.CaptureAutomationSnapshot().Nodes.Single(node => node.OptionKey == "9999").Id);
await page.GetByTestId("list").GetByItemKey("0").ClickAsync();
await page.GetByTestId("list").GetByItemKey("9999").ClickAsync();
await context.Automation.RunOnUiThreadAsync(surface =>
{
    UiAutomationId fresh = surface.CaptureAutomationSnapshot().Nodes.Single(node => node.OptionKey == "9999").Id;
    if (fresh == evicted)
    {
        throw new InvalidOperationException("An evicted item reused its old attachment identity.");
    }

    var list = (ListBoxElement<string>)surface.RootElement;
    ScrollController scroll = ((ScrollViewElement)list.Children[0]).Controller;
    float before = scroll.Offset;
    surface.Rebuild(CreateList(new[] { "inserted" }.Concat(items).ToArray()));
    surface.UpdateLayout();
    if (scroll.Offset != before + 24 || list.SelectedKey != "9999")
    {
        throw new InvalidOperationException("Reset did not restore the first visible key anchor and selected model key.");
    }

    if (list.Children[0].Children[0].Children.Count > 15)
    {
        throw new InvalidOperationException("Reset exceeded the viewport, overscan and single-pin structural bound.");
    }

    return true;
});
await page.GetByTestId("list").ScrollAsync(new(0, -48));
await context.Automation.RunOnUiThreadAsync(surface =>
{
    surface.CaptureAutomationSnapshot();
    ScrollController scroll = ((ScrollViewElement)surface.RootElement.Children[0]).Controller;
    if (scroll.Offset != 239736)
    {
        throw new InvalidOperationException("Wheel input did not commit through the ordinary lease and viewport.");
    }

    return true;
});
await app.StopAsync();
await running;
Console.WriteLine("Fixed-extent bounded realization and lease-owned key selection passed.");
return 0;
