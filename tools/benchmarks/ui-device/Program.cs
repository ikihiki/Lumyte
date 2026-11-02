using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Wgpu;
using Lumyte.Platform.Windows;
using Lumyte.Signals;
using Lumyte.UI;
using Lumyte.UI.Application;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("This native-window harness requires Windows.");
string output = Path.GetFullPath(Option("output", "artifacts/ui-device/results.jsonl"));
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, string.Empty);
bool verify = args.Contains("--verify");
int warmup = int.Parse(Option("warmup", verify ? "2" : "10"));
int frames = int.Parse(Option("frames", verify ? "2" : "60"));
if (warmup < 0 || frames < 1) throw new ArgumentOutOfRangeException(nameof(frames));
string label = Option("label", "unspecified");
int[] counts = Option("counts", "1,10,100").Split(',').Select(int.Parse).ToArray();
if (counts.Any(count => count < 1 || count > 10000)) throw new ArgumentOutOfRangeException(nameof(counts));
if (args.Contains("--application"))
{
    await ApplicationBenchmark.RunAsync(counts[0], warmup, frames, output, label, !args.Contains("--static"), args.Contains("--all-changing"), args.Contains("--layout-changing"), args.Contains("--detail"));
    return;
}
Exception? failure = null;
var thread = new Thread(() => { try { Run(); } catch (Exception error) { failure = error; } });
thread.SetApartmentState(ApartmentState.STA);
thread.Start();
if (!thread.Join(TimeSpan.FromMinutes(5))) Environment.Exit(2);
if (failure is not null) throw failure;

string Option(string name, string fallback) => args.FirstOrDefault(arg => arg.StartsWith($"--{name}=", StringComparison.Ordinal))?[(name.Length + 3)..] ?? fallback;

void Output(object value)
{
    string json = JsonSerializer.Serialize(value);
    File.AppendAllText(output, json + Environment.NewLine);
    Console.WriteLine(json);
}

void Run()
{
    using var platform = new WindowsPlatform();
    using var window = platform.CreateWindow(new() { Title = "Lumyte UI device performance", ClientSize = new(800, 600) });
    window.Show();
    platform.PumpEvents();
    using var retention = window.RetainNativeTarget();
    using var device = WgpuDevice.Create();
    object adapter = typeof(WgpuDevice).GetField("_adapter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(device)!;
    object info = adapter.GetType().GetMethod("GetInfo", Type.EmptyTypes)!.Invoke(adapter, null)!;
    Output(new { Kind = "adapter", Label = label, Values = info.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(info)?.ToString()) });
    var native = (WindowsWindow)window;
    using var graphics = device.CreateSurface(Ahjo.Wgpu.SurfaceSource.WindowsHwnd(native.HInstance, native.Hwnd));
    SwapchainDesc configuration = UiHostPresenter.Configuration(graphics, window.State.PixelSize.Width, window.State.PixelSize.Height);
    if (Option("present", "fifo") == "immediate") configuration = configuration with { PresentMode = PresentMode.Immediate };
    using var swapchain = graphics.CreateSwapchain(configuration);
    using var presenter = new UiHostPresenter(device, new(1, 1, 1, 1));
    // The optional local instrumentation patch exposes phase timings. Normal
    // builds have no benchmark callback or per-frame timing overhead.
    PropertyInfo? phase = typeof(UiHostPresenter).GetProperty("Phase", BindingFlags.NonPublic | BindingFlags.Static);
    Output(new { Kind = "environment", Label = label, HarnessVersion = 2, Runtime = Environment.Version.ToString(), OS = Environment.OSVersion.ToString(), Pixels = window.State.PixelSize, Scale = window.State.Scale.ToString(), Configuration = swapchain.Configuration, Warmup = warmup, Samples = frames, PhasesInstrumented = phase is not null, TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"), ServerGc = System.Runtime.GCSettings.IsServerGC });
    if (args.Contains("--queue-probe"))
    {
        var timings = new List<double>();
        for (int i = -warmup; i < frames; i++)
        {
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            commands.Finish();
            long started = Stopwatch.GetTimestamp();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            Task pending = submission.WaitAsync().AsTask();
            PumpUntil(pending, started);
            pending.GetAwaiter().GetResult();
            if (i >= 0) timings.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }

        Output(new { Kind = "queue-probe", Label = label, Milliseconds = timings });
        window.Close();
        return;
    }
    foreach (int count in counts)
    foreach (bool changing in new[] { false, true })
    {
        var color = Signal.Create(new Color2D(1, 0, 0, 1));
        int columnCount = Math.Min(count, 100);
        int rows = (count + columnCount - 1) / columnCount;
        Widget[] columns = Enumerable.Range(0, columnCount).Select(column => (Widget)Stack()[Enumerable.Range(0, rows).Where(row => column * rows + row < count).Select(row => (Widget)Rectangle(width: 800f / columnCount, height: 600f / rows, fill: column * rows + row == count - 1 ? color : new Color2D(1, 0, 0, 1))).ToArray()]).ToArray();
        using var surface = new UiSurface(Stack(orientation: UiOrientation.Horizontal)[columns]);
        surface.SetViewport(window.State.ClientSize);
        surface.UpdateLayout();
        DrawList2D Record()
        {
            var canvas = new Canvas2D();
            canvas.Begin();
            canvas.Scale(window.State.Scale);
            canvas.ClipRect(new(0, 0, window.State.ClientSize.X, window.State.ClientSize.Y));
            surface.Record(canvas);
            return canvas.End();
        }

        DrawList2D cached = Record();
        var samples = new List<object>();
        Guid owner = Guid.NewGuid();
        for (int frame = -warmup; frame < frames; frame++)
        {
            platform.PumpEvents();
            var phases = new Dictionary<string, double>();
            phase?.SetValue(null, (Action<string, double>)((name, value) => phases[name] = value));
            long allocated = GC.GetTotalAllocatedBytes(false);
            long start = Stopwatch.GetTimestamp();
            if (changing) color.Value = frame % 2 == 0 ? new(1, 0, 0, 1) : new(0, 0, 1, 1);
            surface.UpdateLayout();
            DrawList2D list = changing ? Record() : cached;
            double cpu = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long draw = Stopwatch.GetTimestamp();
            Task<SurfaceStatus> pending = presenter.DrawAsync(swapchain, list, owner);
            PumpUntil(pending, draw);
            SurfaceStatus status = pending.GetAwaiter().GetResult();
            if (status != SurfaceStatus.Success) throw new InvalidOperationException($"Unexpected surface status: {status}");
            double drawMs = Stopwatch.GetElapsedTime(draw).TotalMilliseconds;
            double total = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long bytes = GC.GetTotalAllocatedBytes(false) - allocated;
            if (frame >= 0) samples.Add(new { Frame = frame, CpuMilliseconds = cpu, DrawAndWaitMilliseconds = drawMs, TotalMilliseconds = total, AllocatedBytes = bytes, Phases = phases });
        }

        Output(new { Kind = "case", Label = label, Name = $"device-{(changing ? "color" : "cached")}-{count}", Warmup = warmup, Frames = samples });
        phase?.SetValue(null, null);
        if (verify)
        {
            UiFramePixels? pixels = null;
            Task<SurfaceStatus> capture = presenter.DrawAsync(swapchain, changing ? Record() : cached, owner, image => pixels = image);
            PumpUntil(capture, Stopwatch.GetTimestamp());
            if (capture.GetAwaiter().GetResult() != SurfaceStatus.Success || pixels is null) throw new InvalidOperationException("Capture failed.");
            float sampleX = ((count - 1) / rows + 0.5f) * 800 / columnCount;
            float sampleY = ((count - 1) % rows + 0.5f) * 600 / rows;
            int offset = ((int)(sampleY * window.State.Scale.Y) * (int)pixels.Width + (int)(sampleX * window.State.Scale.X)) * 4;
            bool blue = changing && (frames - 1) % 2 != 0;
            if (pixels.Pixels[offset + (blue ? 2 : 0)] < 250 || pixels.Pixels[offset + (blue ? 0 : 2)] > 5) throw new InvalidOperationException("Unexpected rectangle color.");
            Output(new { Kind = "pixels", Label = label, Name = $"{count}-{changing}", Width = pixels.Width, Height = pixels.Height, Hash = Convert.ToHexString(SHA256.HashData(pixels.Pixels)) });
        }
    }

    phase?.SetValue(null, null);
    window.Close();

    void PumpUntil(Task pending, long started)
    {
        while (!pending.IsCompleted)
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(30)) throw new TimeoutException("A native frame did not finish within 30 seconds.");
            platform.PumpEvents();
            device.ProcessEvents();
            Thread.Sleep(1);
        }
    }
}
