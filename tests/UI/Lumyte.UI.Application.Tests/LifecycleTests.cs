using Lumyte.UI.Application.Headless;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Exercises actual headless owner turns and lifecycle boundaries.</summary>
public sealed class LifecycleTests
{
    /// <summary>Checks Build freezes registrations without invoking user or driver startup factories.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task BuildIsPassiveAndFreezesConfigurationAsync()
    {
        int calls = 0;
        string[] args = ["original"];
        UiApplicationBuilder builder = UiApplication.CreateBuilder(args);
        builder.UseHeadless().UseRoot(_ =>
        {
            calls++;
            return Panel();
        });
        builder.Services.AddSingleton(_ => new UnresolvedService(() => calls++));
        await using UiApplication app = builder.Build();
        args[0] = "changed";
        Assert.Equal("original", app.Arguments[0]);
        Assert.Equal(0, calls);
        Assert.Equal(UiApplicationState.Created, app.State);
        Assert.False(app.Dispatcher.CheckAccess());
        Assert.Throws<InvalidOperationException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(() => builder.Services.AddSingleton<object>());
        Assert.Throws<InvalidOperationException>(() => app.Dispatcher.InvokeAsync(() => calls++));
    }

    /// <summary>Checks callbacks and queued queries use one owner, always queue, and retain FIFO ordering.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task DispatcherQueuesOnTheOwnerAndPreservesOrderAsync()
    {
        UiApplicationWindowContext? context = null;
        UiApplicationBuilder builder = Builder();
        builder.UseRoot(value =>
        {
            context = value;
            return Button(width: 20, height: 20);
        });
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await RunningAsync(app);
        var order = new List<int>();
        int owner = await app.Dispatcher.InvokeAsync(() => Environment.CurrentManagedThreadId);
        Assert.NotEqual(Environment.CurrentManagedThreadId, owner);
        Task first = app.Dispatcher.InvokeAsync(() => order.Add(1)).AsTask();
        Task second = app.Dispatcher.InvokeAsync(() => order.Add(2)).AsTask();
        await Task.WhenAll(first, second);
        ValueTask nested = default;
        await app.Dispatcher.InvokeAsync(() =>
        {
            Assert.True(app.Dispatcher.CheckAccess());
            nested = app.Dispatcher.InvokeAsync(() => order.Add(3));
            Assert.False(nested.IsCompleted);
        });
        await nested;
        Assert.Equal([1, 2, 3], order);
        Assert.False(context!.Automation is Lumyte.UI.Automation.IUiAutomationScreenshotHost);
        await context.Automation.NextFrameAsync();
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await running;
        Assert.Equal(UiApplicationState.Stopped, app.State);
    }

    /// <summary>Checks retirement failure retains the view and resumes cleanup without repeating successful stages.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task RetirementFailureCanBeRetriedWithoutDoubleReleaseAsync()
    {
        var content = new Content { FailRetireOnce = true };
        UiApplicationBuilder builder = Builder();
        builder.UseContent(_ => content);
        UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await RunningAsync(app);
        await Assert.ThrowsAnyAsync<Exception>(() => app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAnyAsync<Exception>(() => running);
        Assert.Equal(UiApplicationState.StopFailed, app.State);
        Assert.False(app.Completion.IsCompleted);
        Assert.Equal(0, content.Releases);
        Assert.True(content.SawSurfaceAtRetire);
        await app.RetryStopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await app.Completion;
        Assert.Equal(UiApplicationState.Stopped, app.State);
        Assert.Equal(1, content.Stops);
        Assert.Equal(2, content.Retires);
        Assert.Equal(1, content.Releases);
        await app.DisposeAsync();
        Assert.Equal(1, content.Releases);
    }

    /// <summary>Checks cleanup can return from ConfigureAwait(false) to the same dispatcher after normal admission closes.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task CleanupAdmissionAllowsOwnerWorkAfterStoppingAsync()
    {
        var content = new Content { ReturnToOwnerDuringRetire = true };
        UiApplicationBuilder builder = Builder();
        builder.UseContent(_ => content);
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await RunningAsync(app);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await running;
        Assert.Equal(content.Owner, content.RetireOwner);
        Assert.Equal(1, content.Releases);
    }

    /// <summary>Checks scope-style final release failures are terminal and are never retried.</summary>
    /// <returns>The asynchronous verification.</returns>
    [Fact]
    public async Task FinalReleaseFailureIsTerminalAndNotRetriedAsync()
    {
        var content = new Content { FailRelease = true };
        UiApplicationBuilder builder = Builder();
        builder.UseContent(_ => content);
        UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await RunningAsync(app);
        await Assert.ThrowsAnyAsync<Exception>(() => app.StopAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAnyAsync<Exception>(() => running);
        Assert.Equal(UiApplicationState.Failed, app.State);
        Assert.Equal(1, content.Releases);
        Assert.Throws<InvalidOperationException>(() => { _ = app.RetryStopAsync(); });
        await Assert.ThrowsAnyAsync<Exception>(() => app.DisposeAsync().AsTask());
        Assert.Equal(1, content.Releases);
    }

    /// <summary>Checks an initialization failure still releases its partial presentation and terminates.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task FailedRootInitializationTerminatesAfterCleanupAsync()
    {
        UiApplicationBuilder builder = Builder().UseRoot(_ => throw new ArithmeticException("initialize"));
        UiApplication app = builder.Build();
        await Assert.ThrowsAsync<ArithmeticException>(() => app.RunAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(UiApplicationState.Failed, app.State);
        await Assert.ThrowsAsync<ArithmeticException>(() => app.DisposeAsync().AsTask());
    }

    /// <summary>Checks a throwing content callback cannot prevent accepted cleanup from progressing.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task FailingAdvanceStopsAndReleasesContentAsync()
    {
        var content = new Content { FailAdvance = true };
        UiApplicationBuilder builder = Builder().UseContent(_ => content);
        UiApplication app = builder.Build();
        await Assert.ThrowsAsync<ArithmeticException>(() => app.RunAsync().WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(UiApplicationState.Failed, app.State);
        Assert.Equal(1, content.Stops);
        Assert.Equal(1, content.Releases);
        await Assert.ThrowsAsync<ArithmeticException>(() => app.DisposeAsync().AsTask());
    }

    /// <summary>Checks a failed DI validation leaves the builder retryable without installing a duplicate dispatcher.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task FailedBuildCanBeCorrectedAndRetriedAsync()
    {
        UiApplicationBuilder builder = Builder().UseRoot(_ => Panel());
        builder.Services.AddSingleton<RequiresDependency>();
        Assert.Throws<AggregateException>(() => builder.Build());
        builder.Services.AddSingleton<Dependency>();
        await using UiApplication app = builder.Build();
        Assert.Equal(UiApplicationState.Created, app.State);
    }

    private static UiApplicationBuilder Builder() => UiApplication.CreateBuilder().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) }).UseHeadless();

    private static async Task RunningAsync(UiApplication app)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (app.State != UiApplicationState.Running)
        {
            if (app.Completion.IsCompleted)
            {
                await app.Completion;
            }

            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class Dependency;

    private sealed class RequiresDependency(Dependency dependency)
    {
        internal Dependency Value { get; } = dependency;
    }

    private sealed class UnresolvedService
    {
        internal UnresolvedService(Action action)
        {
            action();
        }
    }

    private sealed class Content : IUiApplicationContent
    {
        private UiApplicationWindowContext? _context;

        public bool IsStopped { get; private set; }

        internal bool FailAdvance { get; init; }

        internal bool FailRetireOnce { get; init; }

        internal bool FailRelease { get; init; }

        internal bool ReturnToOwnerDuringRetire { get; init; }

        internal int Owner { get; private set; }

        internal int RetireOwner { get; private set; }

        internal int Stops { get; private set; }

        internal int Retires { get; private set; }

        internal int Releases { get; private set; }

        internal bool SawSurfaceAtRetire { get; private set; }

        public ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken)
        {
            _context = context;
            Owner = Environment.CurrentManagedThreadId;
            return context.View.SetInitialRootAsync(Panel(), cancellationToken);
        }

        public void Advance()
        {
            if (FailAdvance)
            {
                throw new ArithmeticException("advance");
            }
        }

        public void BeginStop()
        {
            Stops++;
            IsStopped = true;
        }

        public async ValueTask RetireAsync()
        {
            Retires++;
            SawSurfaceAtRetire = _context!.View.CurrentSurface is not null;
            if (FailRetireOnce && Retires == 1)
            {
                throw new ArithmeticException("retire");
            }

            if (ReturnToOwnerDuringRetire)
            {
                await Task.Delay(1).ConfigureAwait(false);
                await _context.Dispatcher.InvokeAsync(() => RetireOwner = Environment.CurrentManagedThreadId);
            }
        }

        public ValueTask ReleaseAsync()
        {
            Releases++;
            Assert.Null(_context!.View.CurrentSurface);
            return FailRelease ? ValueTask.FromException(new ArithmeticException("release")) : ValueTask.CompletedTask;
        }
    }
}
