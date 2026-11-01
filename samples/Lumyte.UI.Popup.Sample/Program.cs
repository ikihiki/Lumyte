using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Input.Abstractions;
using Lumyte.UI;
using Lumyte.UI.Automation;
using Lumyte.UI.SampleSupport;
using Lumyte.UI.Windowing;
using Lumyte.UI.Windowing.Automation;
using static Lumyte.UI.Compose;

string backend = args.Length > 0 ? args[0] : "none";
if (backend is not ("none" or "vulkan" or "wgpu"))
{
    throw new ArgumentException("Use none, vulkan or wgpu.");
}

using VulkanDevice? vulkan = backend == "vulkan" ? VulkanDevice.Create() : null;
using WgpuDevice? wgpu = backend == "wgpu" ? WgpuDevice.Create() : null;
IGraphicDevice? device = vulkan is not null ? vulkan : wgpu;
Definitions.Button anchor = Button(width: 20, height: 20, testId: "owner")[Rectangle(width: 20, height: 20, fill: new Color2D(0, 0, 1, 1))];
using var surface = new UiSurface(Panel()[anchor]);
var origin = new InputWindowId(Guid.NewGuid());
var scope = Guid.NewGuid();
await using var exercise = new UiPopupExercise(surface, surface.RootElement.Children[0], () => new(1, origin, new(400, 300), Vector2.One, true, true, false, scope));
void Frame()
{
    exercise.Advance();
    var canvas = new Canvas2D();
    canvas.Begin();
    exercise.Source.Record(canvas);
    _ = canvas.End();
    exercise.Source.CompleteFrame();
}

for (int i = 0; i < 10; i++)
{
    Frame();
}

exercise.Open();
for (int i = 0; i < 15; i++)
{
    Frame();
}

if (exercise.Current?.State != UiPopupState.Open)
{
    throw new InvalidOperationException("Popup did not open.");
}

using var domain = new SemaphoreSlim(1, 1);
var hosts = new List<UiSampleAutomationHost>();
IUiAutomationHost Factory(UiSurface target)
{
    var host = new UiSampleAutomationHost(
        target,
        token =>
    {
        token.ThrowIfCancellationRequested();
        Frame();
        return ValueTask.CompletedTask;
    },
        inputDomain: domain);
    hosts.Add(host);
    return host;
}

await using (var session = new UiPopupAutomationSession(exercise.Popups, Factory(surface), Factory))
{
    UiAutomationPage popup = session.Popup(exercise.Current.Id);
    await popup.GetByTestId("popup-save").ClickAsync();
    await popup.GetByTestId("popup-save").PressAsync(Key.Enter);
    await popup.Expect(popup.GetByTestId("popup-save")).ToBeFocusedAsync();
    if (device is not null)
    {
        UiWindowId window = exercise.Windows.CaptureWindows().Single(s => s.Role == UiWindowRole.Popup).Id;
        byte[] png = await exercise.CapturePngAsync(device, window, CancellationToken.None);
        string path = args.Length > 1 ? args[1] : "popup-" + backend + ".png";
        await File.WriteAllBytesAsync(path, png);
        Console.WriteLine($"Popup PNG: {path}");
    }

    await popup.GetByTestId("submenu").ClickAsync();
    for (int i = 0; i < 15; i++)
    {
        Frame();
    }

    UiPopupInfo child = (await session.ListPopupsAsync()).Single(p => p.ParentPopupId is not null);
    await session.Popup(child.Id).GetByTestId("child-save").ClickAsync();
    exercise.Current.Close();
    for (int i = 0; i < 15; i++)
    {
        Frame();
    }
}

foreach (UiSampleAutomationHost host in hosts)
{
    await host.DisposeAsync();
}

if (exercise.Clicks != 3)
{
    throw new InvalidOperationException("Unexpected popup click count.");
}

Console.WriteLine("Popup automation passed: owner, submenu, Click, Enter, cleanup; saves=3.");
