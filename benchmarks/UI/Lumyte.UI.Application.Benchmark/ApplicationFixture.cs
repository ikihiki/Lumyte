using Lumyte.Mathematics.Colors;
using Lumyte.Signals;
using Lumyte.UI.Application.Diagnostics;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Automation;
using Microsoft.Extensions.DependencyInjection;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Benchmark;

/// <summary>Public headless owner fixture with a caller-driven monotonic clock and barrier count.</summary>
public sealed class ApplicationFixture : IAsyncDisposable
{
    private readonly Clock _clock = new();
    private readonly TaskCompletionSource<UiApplicationWindowContext> _opened = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Signal<SRgba<float>> _color = Signal.Create(new SRgba<float>(1, 0, 0, 1));
    private Task? _running;
    private UiApplicationWindowContext? _context;
    private bool _alternate;

    /// <summary>Initializes a new instance of the <see cref="ApplicationFixture"/> class.</summary>
    /// <param name="elements">Actual number of controls.</param>
    /// <param name="diagnostics">Whether bounded local diagnostics are enabled; never adds network transport.</param>
    public ApplicationFixture(int elements, bool diagnostics)
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless(new() { ClientSize = new(100, elements * 10) })
            .Configure(new() { TimeProvider = _clock, FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.Services.AddSingleton<Marker>();
        if (diagnostics)
        {
            builder.UseDiagnostics(new() { TargetName = "benchmark", Runtime = new() { MaximumTargets = 4, MaximumJobs = 4, MaximumRunningJobs = 1, MaximumArtifactBytes = 65536, MaximumStoredArtifactBytes = 65536 } });
        }

        builder.UseWindows(applicationContext => [Compose.Window(
            "main",
            "CPU retained host",
            root: context =>
        {
            _context = context;
            _ = context.Services.GetRequiredService<Marker>();
            Definitions.Stack root = Stack();
            root.Children.Initialize(Enumerable.Range(0, elements).Select(index => Button(testId: $"control-{index}", width: 40, height: 10)[Rectangle(width: 40, height: 10, fill: _color)]).ToArray());
            return root;
        },
            onOpened: _ => _opened.TrySetResult(_context!))]);
        App = builder.Build();
    }

    /// <summary>Gets the non-reusable application.</summary>
    public UiApplication App { get; }

    /// <summary>Gets completed requested owner frame barriers, not capture sequences.</summary>
    public int FrameOrdinal { get; private set; }

    /// <summary>Gets the actual alternating model value; callers inspect through the owner.</summary>
    public SRgba<float> ModelColor => _color.Value;

    /// <summary>Gets the real input/queue domain after startup.</summary>
    public IUiAutomationHost Host => _context!.Automation;

    /// <summary>Starts the dedicated owner and completes its first requested frame.</summary>
    /// <returns>Actual initialization and frame completion.</returns>
    public async Task StartAsync()
    {
        _running = App.RunAsync();
        Task winner = await Task.WhenAny(_opened.Task, _running);
        if (winner == _running)
        {
            await _running;
            throw new InvalidOperationException("Application stopped before opening.");
        }

        _context = await _opened.Task;
        await AdvanceAsync();
    }

    /// <summary>Mutates the real signal on its owner and completes a newly admitted frame.</summary>
    /// <returns>Completed barrier ordinal.</returns>
    public async Task<int> AdvanceAsync()
    {
        await App.Dispatcher.InvokeAsync(() =>
        {
            _alternate = !_alternate;
            _color.Value = _alternate ? new(0, 1, 0, 1) : new(1, 0, 0, 1);
        });
        Task frame = Host.NextFrameAsync().AsTask();
        await App.Dispatcher.InvokeAsync(static () => { });
        _clock.Advance();
        await App.Dispatcher.InvokeAsync(static () => { });
        await frame;
        return ++FrameOrdinal;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        if (_running is not null)
        {
            await _running;
        }

        await App.DisposeAsync();
    }

    private sealed class Marker;

    private sealed class Clock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        internal void Advance() => Interlocked.Add(ref _ticks, TimeSpan.TicksPerMillisecond);
    }
}
