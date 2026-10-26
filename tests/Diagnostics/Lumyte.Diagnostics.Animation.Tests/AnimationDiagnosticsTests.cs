namespace Lumyte.Diagnostics.Animation.Tests;

/// <summary>Checks the scoped animation registry and generated diagnostic operations.</summary>
public sealed class AnimationDiagnosticsTests
{
    private static readonly HashSet<DiagnosticPermission> _observe = [DiagnosticPermission.Observe];
    private static readonly HashSet<DiagnosticPermission> _edit = [DiagnosticPermission.Edit];
    private static readonly HashSet<DiagnosticPermission> _none = [];
    private static readonly Dictionary<string, DiagnosticValue> _noArguments = new(StringComparer.Ordinal);

    private enum Motion
    {
        Idle,
        Action,
    }

    /// <summary>Checks the adapter joins an existing scoped diagnostic execution point.</summary>
    [Fact]
    public void RegistersScopedDiagnosticSubsystem()
    {
        var services = new ServiceCollection();
        services.AddLumyteDiagnostics(static options => options.Enabled = false);
        services.AddDiagnosticExecutionPoint<BeforeAnimationUpdate>("animation.before-update");
        services.AddAnimationDiagnostics<BeforeAnimationUpdate>();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        IDiagnosticPump<BeforeAnimationUpdate> pump = scope.ServiceProvider.GetRequiredService<IDiagnosticPump<BeforeAnimationUpdate>>();
        pump.Activate();
        DiagnosticSubsystemCatalog catalog = Assert.Single(pump.Catalog);
        Assert.Equal("animation", catalog.Subsystem.Id);
        Assert.Contains(catalog.Operations, operation => operation.Id == "read");
        Assert.Same(
            scope.ServiceProvider.GetRequiredService<AnimationDiagnosticRegistry>(),
            scope.ServiceProvider.GetRequiredService<AnimationDiagnosticRegistry>());
    }

    /// <summary>Checks playback snapshots and controls use the registered execution object.</summary>
    [Fact]
    public void ControlsRegisteredPlayback()
    {
        var clock = new ManualClock();
        var channel = AnimationChannel<float>.Create();
        var source = new Tween<float>(0, 1, Duration.FromTicks(10), AnimationInterpolators.Float, AnimationEasing.Linear);
        AnimationTimeline timeline = ComposeAnimation.Timeline()[ComposeAnimation.Track<float>(channel, source)].Build();
        var playback = new AnimationPlayback(clock, timeline);
        var registry = new AnimationDiagnosticRegistry();
        using AnimationDiagnosticRegistration registration = registry.Register("character.main", playback);
        DiagnosticOperationSet operations = Operations(registry);

        DiagnosticOperationResult list = Invoke(operations, "list", _noArguments, _observe);
        Assert.Equal("character.main", list.Values!["targets"].String);
        DiagnosticOperationResult read = Invoke(operations, "read", Args(("id", "character.main")), _observe);
        Assert.Equal("playback", read.Values!["kind"].String);
        Assert.Equal("Stopped", read.Values["status"].String);

        Assert.Equal("success", Invoke(operations, "play", Args(("id", "character.main")), _edit).Status);
        Assert.Equal(AnimationPlaybackState.Playing, playback.State);
        Assert.Equal("success", Invoke(operations, "seek", Args(("id", "character.main"), ("position-ticks", 5L)), _edit).Status);
        Assert.Equal(Duration.FromTicks(5), playback.Position);
        Assert.Equal("success", Invoke(operations, "set-speed", Args(("id", "character.main"), ("speed", 2.0)), _edit).Status);
        Assert.Equal(2, playback.Speed);
        Assert.Equal("success", Invoke(operations, "pause", Args(("id", "character.main")), _edit).Status);
        Assert.Equal(AnimationPlaybackState.Paused, playback.State);
        Assert.Equal("success", Invoke(operations, "stop", Args(("id", "character.main")), _edit).Status);
        Assert.Equal(AnimationPlaybackState.Stopped, playback.State);
    }

    /// <summary>Checks state formatting and only explicitly exposed triggers are available.</summary>
    [Fact]
    public void ControlsRegisteredStateMachine()
    {
        var clock = new ManualClock();
        var trigger = StateMachineTrigger.Create();
        var builder = new AnimationStateMachineBuilder<Motion, string>();
        builder.AddState(Motion.Idle, ComposeAnimation.Timeline()[ComposeAnimation.Delay(Duration.FromTicks(10))].Build());
        builder.AddState(Motion.Action, ComposeAnimation.Timeline()[ComposeAnimation.Delay(Duration.FromTicks(10))].Build());
        builder.AddTransition(Motion.Idle, Motion.Action, trigger: trigger);
        AnimationStateMachine<Motion, string> machine = builder.Build(clock, Motion.Idle, "input");
        var registry = new AnimationDiagnosticRegistry();
        using AnimationDiagnosticRegistration registration = registry.Register(
            "character.motion",
            machine,
            static state => state.ToString(),
            new Dictionary<string, StateMachineTrigger> { ["action"] = trigger });
        DiagnosticOperationSet operations = Operations(registry);

        DiagnosticOperationResult read = Invoke(operations, "read", Args(("id", "character.motion")), _observe);
        Assert.Equal("state-machine", read.Values!["kind"].String);
        Assert.Equal("Idle", read.Values["state"].String);
        Assert.Equal("unsupported", Invoke(operations, "set-trigger", Args(("id", "character.motion"), ("trigger", "hidden")), _edit).Code);
        Assert.Equal("success", Invoke(operations, "set-trigger", Args(("id", "character.motion"), ("trigger", "action")), _edit).Status);

        Assert.True(machine.Update("input", new AnimationOutput(), [], []));
        Assert.Equal(Motion.Action, machine.CurrentState);
        Assert.Equal("success", Invoke(operations, "pause", Args(("id", "character.motion")), _edit).Status);
        Assert.Equal(AnimationStateMachineStatus.Paused, machine.Status);
        Assert.Equal("success", Invoke(operations, "play", Args(("id", "character.motion")), _edit).Status);
        Assert.Equal(AnimationStateMachineStatus.Running, machine.Status);
        Assert.Equal("unsupported", Invoke(operations, "seek", Args(("id", "character.motion"), ("position-ticks", 0L)), _edit).Code);
        Assert.Equal("success", Invoke(operations, "clear-triggers", Args(("id", "character.motion")), _edit).Status);
        DiagnosticOperationResult stopped = Invoke(operations, "stop", Args(("id", "character.motion")), _edit);
        Assert.Equal("success", stopped.Status);
        Assert.Equal("Stopped", stopped.Values!["status"].String);
        Assert.Equal(0, stopped.Values["position-ticks"].Int64);
        Assert.Equal("unsupported", Invoke(operations, "play", Args(("id", "character.motion")), _edit).Code);
    }

    /// <summary>Checks registration validation, permissions and lease-safe removal.</summary>
    [Fact]
    public void BoundsRegistryAndDiagnosticAccess()
    {
        var registry = new AnimationDiagnosticRegistry();
        AnimationPlayback playback = Playback();
        Assert.Throws<ArgumentException>(() => registry.Register("Invalid ID", playback));
        AnimationDiagnosticRegistration registration = registry.Register("first", playback);
        Assert.Throws<ArgumentException>(() => registry.Register("first", Playback()));
        DiagnosticOperationSet operations = Operations(registry);
        Assert.Equal("forbidden", Invoke(operations, "read", Args(("id", "first")), _none).Code);
        Assert.Equal("not-found", Invoke(operations, "read", Args(("id", "missing")), _observe).Code);
        Assert.Equal("unsupported", Invoke(operations, "set-trigger", Args(("id", "first"), ("trigger", "go")), _edit).Code);
        registration.Dispose();
        registration.Dispose();
        Assert.Equal(string.Empty, Invoke(operations, "list", _noArguments, _observe).Values!["targets"].String);
    }

    private static AnimationPlayback Playback()
        => new(new ManualClock(), ComposeAnimation.Timeline()[ComposeAnimation.Delay(Duration.FromTicks(1))].Build());

    private static DiagnosticOperationSet Operations(AnimationDiagnosticRegistry registry)
        => new((IDiagnosticContributor)(object)new AnimationDiagnostics(registry));

    private static Dictionary<string, DiagnosticValue> Args(params (string Name, object Value)[] values)
    {
        var result = new Dictionary<string, DiagnosticValue>(StringComparer.Ordinal);
        foreach ((string name, object value) in values)
        {
            DiagnosticValue scalar = value switch
            {
                string text => DiagnosticValue.From(text),
                long integer => DiagnosticValue.From(integer),
                double number => DiagnosticValue.From(number),
                _ => throw new InvalidOperationException(),
            };
            result.Add(name, scalar);
        }

        return result;
    }

    private static DiagnosticOperationResult Invoke(
        DiagnosticOperationSet operations,
        string operation,
        IReadOnlyDictionary<string, DiagnosticValue> arguments,
        IReadOnlySet<DiagnosticPermission> permissions)
        => operations.Invoke(operation, arguments, new(Guid.NewGuid(), Guid.NewGuid(), new(1, 1), "test", null, default), permissions);

    private sealed class BeforeAnimationUpdate;
}
