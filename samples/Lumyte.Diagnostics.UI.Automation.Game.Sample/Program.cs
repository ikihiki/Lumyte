using System.Diagnostics;
using System.Numerics;
using Lumyte.Diagnostics;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.Transport.Http;
using Lumyte.Diagnostics.Transport.MagicOnion;
using Lumyte.Diagnostics.UI.Automation;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.UI;
using Lumyte.UI.Automation;
using Lumyte.UI.SampleSupport;
using Microsoft.Extensions.DependencyInjection;

string transport = args.Length > 0 ? args[0] : "http";
var address = new Uri(args.Length > 1 ? args[1] : "http://127.0.0.1:5000");
string gpu = args.Length > 2 ? args[2] : "none";
string token = Environment.GetEnvironmentVariable("LUMYTE_DIAGNOSTICS_GAME_TOKEN") ?? throw new InvalidOperationException("Set the game enrollment token.");
var services = new ServiceCollection();
services.AddLumyteDiagnostics(options =>
{
    options.Enabled = true;
    options.AllowedLogCategoryPrefixes = ["Lumyte.Diagnostics.UI.Automation"];
    options.AllowedActivitySourceNames = ["Lumyte.Diagnostics.UI.Automation"];
    options.TraceSampleRatio = 1;
});
services.AddDiagnosticExecutionPoint<BeforeUiAutomation>("ui.before-automation");
services.AddUiAutomationDiagnostics<BeforeUiAutomation>();
services.AddDiagnosticAgent<BeforeUiAutomation>();
if (transport == "http")
{
    services.AddHttpDiagnosticTransport(options =>
    {
        options.BaseAddress = address;
        options.GameToken = token;
    });
}
else if (transport == "magiconion")
{
    services.AddMagicOnionDiagnosticTransport(options =>
    {
        options.Endpoint = address;
        options.GameToken = token;
    });
}
else
{
    throw new ArgumentException("Choose http or magiconion.");
}

await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using AsyncServiceScope scope = provider.CreateAsyncScope();
IServiceProvider game = scope.ServiceProvider;
UiAutomationDiagnosticsRuntime runtime = game.GetRequiredService<UiAutomationDiagnosticsRuntime>();
IDiagnosticPump<BeforeUiAutomation> pump = game.GetRequiredService<IDiagnosticPump<BeforeUiAutomation>>();
DiagnosticAgent<BeforeUiAutomation> agent = game.GetRequiredService<DiagnosticAgent<BeforeUiAutomation>>();
using VulkanDevice? vulkan = gpu == "vulkan" ? VulkanDevice.Create() : null;
using WgpuDevice? wgpu = gpu == "wgpu" ? WgpuDevice.Create() : null;
IGraphicDevice? device = vulkan is not null ? vulkan : wgpu;
int saves = 0;
using UiSurface surface = UiAutomationExercise.CreateSurface(() => Console.WriteLine($"Save count: {++saves}"));
surface.SetViewport(new(240, 160));
if (device is not null)
{
    // Prepare the backend pipeline before admitting jobs with a ten-second execution limit.
    _ = UiSampleScreenshot.CaptureAsync(device, surface, Vector2.One, CancellationToken.None).AsTask().GetAwaiter().GetResult();
}

var host = new UiQueuedAutomationHost(surface);
IUiAutomationHost published = device is null ? host : new UiSampleScreenshotHost(host, CaptureAsync);
using UiAutomationDiagnosticRegistration registration = runtime.Register("settings", surface, published, new() { AllowInput = true, AllowScreenshots = device is not null });
game.GetRequiredService<DiagnosticTelemetry>().Start();
pump.Activate();
long frame = 0;
try
{
    using var connect = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    agent.ConnectAsync(connect.Token).GetAwaiter().GetResult();
    Console.WriteLine($"Connected: {transport}, session={agent.SessionId:D}");
    Task run = agent.RunAsync();
    long started = Stopwatch.GetTimestamp();
    while (!run.IsCompleted && Stopwatch.GetElapsedTime(started) < TimeSpan.FromMinutes(2))
    {
        var current = new DiagnosticFrame(frame++, Stopwatch.GetTimestamp());
        pump.Pump(current, new(TimeSpan.FromMilliseconds(2), 16));
        runtime.Advance(current);
        host.AdvanceFrame();
        Thread.Sleep(5);
    }

    if (run.IsFaulted)
    {
        _ = run.Exception;
        Console.WriteLine("Diagnostic connection ended; stopping UI jobs.");
    }
}
finally
{
    runtime.NotifySessionClosed(agent.SessionId);
    runtime.BeginStop();
    while (!runtime.IsStopped)
    {
        runtime.Advance(new(frame++, Stopwatch.GetTimestamp()));
        host.AdvanceFrame();
        Thread.Sleep(1);
    }

    host.DisposeAsync().AsTask().GetAwaiter().GetResult();
    runtime.Dispose();
    pump.Deactivate();
    agent.DisposeAsync().AsTask().GetAwaiter().GetResult();
    Console.WriteLine("UI diagnostic cleanup completed.");
}

async ValueTask<byte[]> CaptureAsync(CancellationToken cancellationToken)
{
    ValueTask<byte[]> capture = await host.RunOnUiThreadAsync(target => UiSampleScreenshot.CaptureAsync(device!, target, Vector2.One, cancellationToken), cancellationToken);
    return await capture;
}
