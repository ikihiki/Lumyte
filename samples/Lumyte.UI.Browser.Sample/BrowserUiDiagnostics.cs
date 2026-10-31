using Lumyte.Diagnostics;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.Transport.Http;
using Lumyte.Diagnostics.UI.Automation;
using Lumyte.UI.Automation;
using Lumyte.UI.SampleSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Browser.Sample;

// A dedicated diagnostic mode: do not forward physical input while this host owns the surface.
internal sealed class BrowserUiDiagnostics : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly AsyncServiceScope _scope;
    private readonly UiAutomationDiagnosticsRuntime _runtime;
    private readonly IDiagnosticPump<Point> _pump;
    private readonly DiagnosticAgent<Point> _agent;
    private readonly UiQueuedAutomationHost _host;
    private Task _connection = Task.CompletedTask;
    private long _frame;

    private BrowserUiDiagnostics(UiSurface surface, string address, string gameToken, Func<CancellationToken, ValueTask<byte[]>>? capture)
    {
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(options => options.Enabled = true);
        services.AddDiagnosticExecutionPoint<Point>("ui.before-automation");
        services.AddUiAutomationDiagnostics<Point>();
        services.AddDiagnosticAgent<Point>();
        services.AddHttpDiagnosticTransport(options =>
        {
            options.BaseAddress = new Uri(address);
            options.GameToken = gameToken;
        });
        _services = services.BuildServiceProvider();
        _scope = _services.CreateAsyncScope();
        IServiceProvider game = _scope.ServiceProvider;
        _runtime = game.GetRequiredService<UiAutomationDiagnosticsRuntime>();
        _pump = game.GetRequiredService<IDiagnosticPump<Point>>();
        _agent = game.GetRequiredService<DiagnosticAgent<Point>>();
        _host = new(surface);
        IUiAutomationHost host = capture is null ? _host : new UiSampleScreenshotHost(_host, capture);
        _runtime.Register("settings", surface, host, new() { AllowInput = true, AllowScreenshots = capture is not null });
        game.GetRequiredService<DiagnosticTelemetry>().Start();
        _pump.Activate();
    }

    public async ValueTask DisposeAsync()
    {
        _runtime.BeginStop();
        while (!_runtime.IsStopped)
        {
            Pump();
            await Task.Delay(1);
        }

        _pump.Deactivate();
        await _agent.DisposeAsync();
        await _connection;
        await _host.DisposeAsync();
        _runtime.Dispose();
        await _scope.DisposeAsync();
        await _services.DisposeAsync();
    }

    internal static async Task<BrowserUiDiagnostics> CreateAsync(UiSurface surface, string address, string gameToken, Func<CancellationToken, ValueTask<byte[]>>? capture = null)
    {
        var diagnostics = new BrowserUiDiagnostics(surface, address, gameToken, capture);
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await diagnostics._agent.ConnectAsync(timeout.Token);
            diagnostics._connection = diagnostics.ObserveConnectionAsync();
            Console.WriteLine("Browser UI diagnostics connected.");
            return diagnostics;
        }
        catch
        {
            await diagnostics.DisposeAsync();
            throw;
        }
    }

    internal void Pump()
    {
        var frame = new DiagnosticFrame(_frame++, 0);
        _pump.Pump(frame, new(TimeSpan.FromMilliseconds(2), 16));
        _runtime.Advance(frame);
        _host.AdvanceFrame();
    }

    private async Task ObserveConnectionAsync()
    {
        try
        {
            await _agent.RunAsync();
        }
        catch (Exception)
        {
            Console.WriteLine("Browser diagnostic connection ended.");
        }
        finally
        {
            _runtime.NotifySessionClosed(_agent.SessionId);
        }
    }

    private sealed class Point;
}
