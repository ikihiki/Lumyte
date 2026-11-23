using Lumyte.Signals;
using Lumyte.UI.Application.Headless;
using Xunit;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Tests;

/// <summary>Integration context is surface-owned, not ambient and not stored on user declarations.</summary>
public sealed class RootEnvironmentTests
{
    private static readonly UiContextKey<object> _context = new();

    /// <summary>Initial, discarded, committed, root-signal and bare rebuild paths retain the same borrowed environment.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task StagedCandidatesAndRootSignalsKeepWindowEnvironmentAsync()
    {
        var probe = new Probe();
        var participant = new Participant();
        var content = new Content(probe);
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.Participants.Add(_ => participant);
        builder.UseContent(_ => content);
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running);
        Task exercise = await app.Dispatcher.InvokeAsync(() => ExerciseAsync(content, probe));
        await exercise.WaitAsync(TimeSpan.FromSeconds(10));
        await app.Dispatcher.InvokeAsync(() =>
        {
            Assert.True(probe.Contexts.Count >= 7);
            Assert.All(probe.Contexts, item => Assert.Same(participant.Values[content.Window!.Id], item));
        });
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>A shared passive root is never mutated with window context or reused as global service identity.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task SharedDeclarationReceivesIndependentWindowEnvironmentsAsync()
    {
        var probe = new Probe();
        Widget root = new Function(probe);
        var participant = new Participant();
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.Participants.Add(_ => participant);
        builder.UseWindows(_ => [Compose.Window("main", "Main", root: _ => root), Compose.Window("child", "Child", root: _ => root)]);
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running);
        await app.Dispatcher.InvokeAsync(() =>
        {
            Assert.Null(root.ProvidedContexts);
            Assert.Equal(2, participant.Values.Count);
            Assert.Equal(2, probe.Contexts.Distinct(ReferenceEqualityComparer.Instance).Count());
        });
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task ExerciseAsync(Content content, Probe probe)
    {
        UiSurface original = content.Window!.View.CurrentSurface!;
        IUiApplicationViewChange discarded = content.Host!.BeginChange();
        await discarded.PrepareAsync(new Function(probe), default);
        await discarded.DiscardAsync(default);
        await discarded.DisposeAsync();
        Assert.Same(original, content.Window.View.CurrentSurface);
        probe.Revision.Value++;
        original.UpdateLayout();
        IUiApplicationViewChange accepted = content.Host.BeginChange();
        await accepted.PrepareAsync(new Function(probe), default);
        await accepted.QuiesceAsync(null, default);
        Assert.True(accepted.TryValidateCommit(out _));
        accepted.Commit(new object());
        await accepted.DrainRetiredAsync(default);
        await accepted.ReleaseRetiredAsync(default);
        await accepted.DisposeAsync();
        UiSurface current = content.Window.View.CurrentSurface!;
        Assert.NotSame(original, current);
        current.SetSignalUpdatesEnabled(true);
        current.Rebuild(new Function(probe));
        current.UpdateLayout();
        probe.Revision.Value++;
        current.UpdateLayout();
        current.Rebuild(new Function(probe));
        current.UpdateLayout();
        probe.Revision.Value++;
        current.UpdateLayout();
    }

    private static async Task WaitAsync(UiApplication app, Func<bool> ready)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (app.State == UiApplicationState.Starting)
        {
            if (app.Completion.IsCompleted)
            {
                await app.Completion;
            }

            await Task.Delay(1, timeout.Token);
        }

        while (!await app.Dispatcher.InvokeAsync(ready, timeout.Token))
        {
            Assert.Null(app.Failure);
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class Probe
    {
        internal Signal<int> Revision { get; } = Signal.Create(0);

        internal List<object> Contexts { get; } = [];
    }

    private sealed class Function(Probe probe) : UiFunctionWidget
    {
        protected override Widget RebuildDeclaration(UiWidgetRebuildContext context) => new Function(probe);

        protected override Widget Build()
        {
            probe.Contexts.Add(Lumyte.UI.Compose.UseContext(_context));
            _ = probe.Revision.Value;
            return Lumyte.UI.Compose.Button(width: 40, height: 20);
        }
    }

    private sealed class Participant : IUiApplicationParticipant
    {
        public bool IsStopped { get; private set; }

        internal Dictionary<Guid, object> Values { get; } = [];

        public ValueTask StartAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public Widget ProvideRootContext(UiApplicationWindowContext context, Widget root)
        {
            if (!Values.TryGetValue(context.Id, out object? value))
            {
                value = new object();
                Values.Add(context.Id, value);
            }

            return Lumyte.UI.Compose.Provide(_context, value, root);
        }

        public void OpenWindow(UiApplicationWindowContext context)
        {
        }

        public void CloseWindow(UiApplicationWindowContext context)
        {
        }

        public bool IsWindowStopped(UiApplicationWindowContext context) => true;

        public void Advance()
        {
        }

        public void BeginStop() => IsStopped = true;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Content(Probe probe) : IUiApplicationContent
    {
        public bool IsStopped { get; private set; }

        internal UiApplicationWindowContext? Window { get; private set; }

        internal IUiApplicationViewHost? Host { get; private set; }

        public async ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken)
        {
            Window = context;
            Host = context.View.CreateViewHost();
            IUiApplicationViewChange change = Host.BeginChange();
            await change.PrepareAsync(new Function(probe), cancellationToken);
            await change.QuiesceAsync(null, cancellationToken);
            Assert.True(change.TryValidateCommit(out _));
            change.Commit(new object());
            await change.DrainRetiredAsync(cancellationToken);
            await change.ReleaseRetiredAsync(cancellationToken);
            await change.DisposeAsync();
        }

        public void Advance()
        {
        }

        public void BeginStop() => IsStopped = true;

        public ValueTask RetireAsync() => ValueTask.CompletedTask;

        public ValueTask ReleaseAsync() => ValueTask.CompletedTask;
    }
}
