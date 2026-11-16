using Lumyte.Core.Time;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Navigation.Benchmark;

/// <summary>Public fixture owning a standard headless application and registered DI pages.</summary>
public sealed class PageApplicationSession : IAsyncDisposable
{
    private readonly Probe _probe = new();
    private readonly Clock _clock = new();
    private readonly UiApplication _app;
    private readonly Task _run;
    private readonly bool _transitions;
    private readonly bool _disabled;
    private ObservedContent _content = null!;
    private int _sequence;
    private int _frames;

    private PageApplicationSession(bool transitions, bool disabled)
    {
        _transitions = transitions;
        _disabled = disabled;
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().Configure(new()
        {
            FrameInterval = TimeSpan.FromTicks(1),
            AnimationClock = _clock,
            Motion = new() { Policy = disabled ? UiMotionPolicy.Disabled : UiMotionPolicy.Full },
        });
        builder.Services.AddSingleton(_probe);
        builder.Services.AddScoped<ScopeLease>();
        builder.Services.AddScoped<Page>();
        builder.MapPage<Page>("/");
        builder.MapPage<Page>("/next");
        var options = new UiNavigationOptions
        {
            MaxHistoryEntries = 2,
            Transitions = transitions ? new() { Enter = FadeIn(Duration.FromSeconds(0.3)) } : null,
        };
        Func<IServiceProvider, IUiApplicationContent> factory = UiPageWindows.Page(new UiPageKey<UiNoArgs>("/"), options);
        builder.UseContent(services => _content = new(factory(services)));
        _app = builder.Build();
        _run = _app.RunAsync();
    }

    /// <summary>Gets the real observed next-frame completion count.</summary>
    public int NextFrames => _frames;

    /// <summary>Gets disposed DI scoped services.</summary>
    public int Disposals => _probe.Disposed;

    /// <summary>Starts and awaits the initial idle publication.</summary>
    /// <param name="transitions">Enables finite transition admission.</param>
    /// <param name="disabled">Disables optional motion by policy.</param>
    /// <returns>The initialized session.</returns>
    public static async Task<PageApplicationSession> CreateAsync(bool transitions, bool disabled = false)
    {
        var session = new PageApplicationSession(transitions, disabled);
        await session.WaitIdleAsync();
        return session;
    }

    /// <summary>Alternates page factories with bounded replacement history.</summary>
    /// <returns>The build count after retirement.</returns>
    public async Task<int> ReplaceAsync()
    {
        await NavigateAsync();
        return await _app.Dispatcher.InvokeAsync(() => _probe.Builds);
    }

    /// <summary>Runs and verifies the actual visual admission outcome.</summary>
    /// <returns>The visual outcome code.</returns>
    public async Task<string> TransitionAsync()
    {
        UiVisualTransitionResult visual = await NavigateAsync();
        Require(_transitions && visual.Status == UiVisualTransitionStatus.Skipped);
        Require(visual.Code == (_disabled ? "motion-disabled" : "capture-unavailable"));
        return visual.Code;
    }

    /// <summary>Rebuilds a visibly different real surface then observes the frame boundary.</summary>
    /// <returns>Observed owner content advances.</returns>
    public async Task<int> MutateAsync()
    {
        await _app.Dispatcher.InvokeAsync(() =>
        {
            int width = 50 + (++_sequence % 23);
            _content.Context.View.CurrentSurface!.Rebuild(Panel(width: width, height: 40)[Button(width: 20, height: 20)]);
        });
        return await StableFrameAsync();
    }

    /// <summary>Waits through a real automation NextFrameAsync completion, not a dispatch-only barrier.</summary>
    /// <returns>Content advances observed on the owner.</returns>
    public async Task<int> StableFrameAsync()
    {
        Task next = await _app.Dispatcher.InvokeAsync(() =>
        {
            _clock.Advance();
            return _content.Context.Automation.NextFrameAsync().AsTask();
        });
        await next.WaitAsync(TimeSpan.FromSeconds(10));
        _frames++;
        return await _app.Dispatcher.InvokeAsync(() => _content.Advances);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await _run.WaitAsync(TimeSpan.FromSeconds(10));
        await _app.DisposeAsync();
        Require(_probe.Created == _probe.Disposed && _probe.Builds == _probe.Created && _probe.Retired == _probe.Created && _probe.ResourcesDisposed == _probe.Created);
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Application page lifecycle oracle failed.");
        }
    }

    private async Task<UiVisualTransitionResult> NavigateAsync()
    {
        UiNavigationRequest request = await _app.Dispatcher.InvokeAsync(() => _probe.Navigation!.RequestNavigate((++_sequence % 2) == 0 ? "/" : "/next", UiNavigationMode.Replace));
        UiNavigationResult logical = await request.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Require(logical.Status == UiNavigationStatus.Committed);
        UiVisualTransitionResult visual = await request.VisualCompletion.WaitAsync(TimeSpan.FromSeconds(10));
        await WaitIdleAsync();
        await _app.Dispatcher.InvokeAsync(() => Require(_probe.Created - _probe.Disposed == 1 && _probe.Created - _probe.Retired == 1 && _probe.Created - _probe.ResourcesDisposed == 1));
        return visual;
    }

    private async Task WaitIdleAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (_app.State == UiApplicationState.Starting)
        {
            if (_app.Completion.IsCompleted)
            {
                await _app.Completion;
            }

            await Task.Delay(1, timeout.Token);
        }

        Require(_app.State == UiApplicationState.Running);
        while (!await _app.Dispatcher.InvokeAsync(() => _probe.Navigation is { } navigation && navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle && _app.State == UiApplicationState.Running, timeout.Token))
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class Clock : IMonotonicClock
    {
        private long _ticks;

        public TimePoint Now => TimePoint.Zero + Duration.FromTicks(_ticks);

        internal void Advance() => _ticks += TimeSpan.TicksPerSecond;
    }

    private sealed class Probe
    {
        internal IUiNavigator? Navigation { get; set; }

        internal int Created { get; set; }

        internal int Builds { get; set; }

        internal int Retired { get; set; }

        internal int Disposed { get; set; }

        internal int ResourcesDisposed { get; set; }
    }

    private sealed class ScopeLease : IDisposable
    {
        private readonly Probe _probe;

        public ScopeLease(Probe probe)
        {
            _probe = probe;
            probe.Created++;
        }

        public void Dispose()
        {
            Require(_probe.ResourcesDisposed == _probe.Disposed + 1);
            _probe.Disposed++;
        }
    }

    private sealed class PageResource(Probe probe) : IDisposable
    {
        public void Dispose() => probe.ResourcesDisposed++;
    }

    private sealed class Page(UiPageContext context, Probe probe, ScopeLease lease) : IUiPage
    {
        public Widget Build(UiPageContext current)
        {
            Require(ReferenceEquals(current, context) && context.Activation.Dispatcher.CheckAccess());
            GC.KeepAlive(lease);
            probe.Navigation = context.Activation.Navigation;
            probe.Builds++;
            current.Own(new PageResource(probe));
            return Panel(width: context.Activation.Destination.Route == "/" ? 40 : 80, height: 40)[Button(width: 20, height: 20)];
        }

        public ValueTask RetireAsync(CancellationToken cancellationToken)
        {
            probe.Retired++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ObservedContent(IUiApplicationContent inner) : IUiApplicationContent, IUiApplicationGraphicsContent
    {
        public bool IsStopped => inner.IsStopped;

        internal UiApplicationWindowContext Context { get; private set; } = null!;

        internal int Advances { get; private set; }

        public ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken)
        {
            Context = context;
            return inner.InitializeAsync(context, cancellationToken);
        }

        public void Advance()
        {
            Advances++;
            inner.Advance();
        }

        public void BeginStop() => inner.BeginStop();

        public ValueTask RetireAsync() => inner.RetireAsync();

        public ValueTask ReleaseAsync() => inner.ReleaseAsync();

        public ValueTask RebuildGraphicsAsync(UiApplicationWindowContext context) => ValueTask.CompletedTask;
    }
}
