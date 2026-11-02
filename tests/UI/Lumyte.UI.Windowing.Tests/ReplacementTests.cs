using System.Numerics;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Windowing.Tests.TestTasks;

namespace Lumyte.UI.Windowing.Tests;

/// <summary>Exercises optional replacement capabilities and public identity projection.</summary>
public sealed class ReplacementTests
{
    /// <summary>Checks candidates remain private, input waits for their own presentation and old binding release cannot retire the new page.</summary>
    /// <param name="fallback">Whether identities pass through the fallback adapter.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementRetainsWindowIdentityAndWaitsForNewGeometry(bool fallback)
    {
        var hosted = new HostedUiWindowManager();
        var native = new HostedUiWindowManager();
        IUiWindowManager windows = fallback ? new FallbackUiWindowManager(native, hosted) : hosted;
        var source = new Source();
        using var oldSurface = new UiSurface(Button());
        using var candidate = new UiSurface(Button());
        UiWindowRegistration registration = windows.Register(source);
        Task<UiWindowBinding> initial = windows.BindPresentationAsync(registration.Window, oldSurface).AsTask();
        Pump(windows, source);
        UiWindowBinding oldBinding = Completed(initial);
        IUiWindowPresentationReplacementManager manager = Assert.IsAssignableFrom<IUiWindowPresentationReplacementManager>(windows);
        IUiWindowPresentationReplacement change = manager.BeginReplacement(registration.Window, oldBinding, candidate);
        Finish(change.PrepareAsync(CancellationToken.None).AsTask());
        Pump(windows, source);
        Assert.True(windows.IsBound(registration.Window, oldSurface));
        Assert.False(windows.IsBound(registration.Window, candidate));
        Assert.True(change.TryValidateCommit(out _));
        source.Acknowledge = false;
        UiWindowBinding binding = change.Commit();
        Assert.True(windows.IsBound(registration.Window, candidate));
        Assert.Null(windows.HitTest(registration.Window, new(10, 10)));
        Finish(change.DrainPreviousAsync(CancellationToken.None).AsTask());
        Finish(change.ReleasePreviousAsync(CancellationToken.None).AsTask());
        Finish(oldBinding.DisposeAsync().AsTask());
        Pump(windows, source);
        Assert.True(windows.IsBound(registration.Window, candidate));
        source.Acknowledge = true;
        Pump(windows, source);
        Assert.Equal(registration.Window, windows.HitTest(registration.Window, new(10, 10))!.Value.Window);
        Finish(change.DisposeAsync().AsTask());
        Task retirement = binding.DisposeAsync().AsTask();
        Pump(windows, source);
        Finish(retirement);
        registration.Dispose();
        Pump(windows, source);
        windows.BeginStop();
        Pump(windows, source);
        windows.Dispose();
        if (fallback)
        {
            native.Dispose();
            hosted.Dispose();
        }
    }

    /// <summary>Checks resize during preparation invalidates a candidate without detaching the old surface.</summary>
    [Fact]
    public void StaleCandidateCanBeDiscardedWithoutReplacingTheOldBinding()
    {
        var windows = new HostedUiWindowManager();
        var source = new Source();
        using var oldSurface = new UiSurface(Panel());
        using var candidate = new UiSurface(Panel());
        UiWindowRegistration registration = windows.Register(source);
        Task<UiWindowBinding> initial = windows.BindPresentationAsync(registration.Window, oldSurface).AsTask();
        Pump(windows, source);
        UiWindowBinding binding = Completed(initial);
        IUiWindowPresentationReplacement change = windows.BeginReplacement(registration.Window, binding, candidate);
        Finish(change.PrepareAsync(CancellationToken.None).AsTask());
        source.Inner.Size = new(100, 100);
        Pump(windows, source);
        Assert.False(change.TryValidateCommit(out string code));
        Assert.Equal("candidate-stale", code);
        Finish(change.DrainCandidateAsync(CancellationToken.None).AsTask());
        Finish(change.DiscardAsync(CancellationToken.None).AsTask());
        Finish(change.DisposeAsync().AsTask());
        Assert.True(windows.IsBound(registration.Window, oldSurface));
        Task retirement = binding.DisposeAsync().AsTask();
        Pump(windows, source);
        Finish(retirement);
        registration.Dispose();
        Pump(windows, source);
        windows.BeginStop();
        Pump(windows, source);
        windows.Dispose();
    }

    private static void Finish(Task task)
    {
        Assert.True(task.IsCompletedSuccessfully);
        task.GetAwaiter().GetResult();
    }

    private static void Pump(IUiWindowManager windows, Source source)
    {
        for (int i = 0; i < 6; i++)
        {
            windows.Advance();
            source.Inner.Present();
        }
    }

    private sealed class Source : IUiWindowSource, IUiWindowPresentationStager
    {
        public Guid SourceId => Inner.SourceId;

        internal TestSource Inner { get; } = new();

        internal bool Acknowledge { set => Inner.Acknowledge = value; }

        public UiWindowSourceSnapshot CaptureState() => Inner.CaptureState();

        public ValueTask<IUiWindowPresentation> PreparePresentationAsync(UiWindowPresentationView view, UiSurface surface, CancellationToken cancellationToken) => Inner.PreparePresentationAsync(view, surface, cancellationToken);

        public bool TryTransformPoint(IUiWindowSource destination, Vector2 position, out Vector2 transformed)
        {
            transformed = position;
            return ReferenceEquals(destination, this);
        }

        public IUiStagedWindowPresentation BeginReplacement(IUiWindowPresentation? current, UiWindowPresentationView view, UiSurface candidate) => new Stage(Inner, view);

        private sealed class Stage(TestSource source, UiWindowPresentationView view) : IUiStagedWindowPresentation
        {
            private readonly TestSource.Presentation _presentation = new(source, view);

            public ValueTask PrepareAsync(CancellationToken cancellationToken)
            {
                source.Presentations.EnsureCapacity(source.Presentations.Count + 1);
                return ValueTask.CompletedTask;
            }

            public bool TryValidateCommit(out string code)
            {
                code = string.Empty;
                return true;
            }

            public IUiWindowPresentation Commit()
            {
                source.Presentations.Add(_presentation);
                return _presentation;
            }

            public ValueTask DrainCandidateAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask DiscardAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
