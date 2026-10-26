using Xunit;

namespace Lumyte.Worlds.Tests;

/// <summary>Tests simulation phases and rendering registration independently of graphics backends.</summary>
public sealed class WorldPhaseTests
{
    /// <summary>Late updates observe completed ordinary updates across scene and world boundaries.</summary>
    [Fact]
    public void EveryUpdateCompletesBeforeAnyLateUpdate()
    {
        var manager = new WorldManager();
        var calls = new List<string>();
        World lower = manager.CreateWorld();
        World upper = manager.CreateWorld();
        var first = new PhaseScene("first", calls);
        var second = new PhaseScene("second", calls);
        var third = new PhaseScene("third", calls);
        first.AfterUpdate = () => Assert.Equal(1, third.UpdateCount);
        lower.AddScene(first);
        lower.AddScene(second);
        upper.AddScene(third);
        manager.Stack.Push(lower);
        manager.Stack.Push(upper);
        manager.BeginFrame();
        var elapsed = TimeSpan.FromMilliseconds(12);
        manager.Stack.Update(elapsed);
        Assert.Equal(new[] { "first:update", "second:update", "third:update", "first:late", "second:late", "third:late" }, calls);
        Assert.Equal(elapsed, first.Elapsed);
        Assert.Equal(elapsed, third.Elapsed);
    }

    /// <summary>Fixed simulation may run zero or multiple times without advancing frame logic.</summary>
    [Fact]
    public void FixedStepsAreIndependentFromFrameUpdates()
    {
        var manager = new WorldManager();
        World world = manager.CreateWorld();
        var scene = new PhaseScene("world", []);
        world.AddScene(scene);
        manager.Stack.Push(world);
        manager.BeginFrame();
        manager.Stack.Update(TimeSpan.FromMilliseconds(5));
        Assert.Equal(0, scene.FixedCount);
        var step = TimeSpan.FromMilliseconds(10);
        manager.Stack.FixedUpdate(step);
        manager.Stack.FixedUpdate(step);
        Assert.Equal(2, scene.FixedCount);
        Assert.Equal(step, scene.Step);
        Assert.Equal(1, scene.UpdateCount);
        Assert.Equal(1, scene.LateCount);
    }

    /// <summary>A pause blocks every simulation phase but preserves render registration.</summary>
    [Fact]
    public void PauseBlocksAllUpdatesWithoutAccumulatingTime()
    {
        var manager = new WorldManager();
        World game = manager.CreateWorld();
        World pause = manager.CreateWorld();
        var gameScene = new PhaseScene("game", []);
        var pauseScene = new PhaseScene("pause", []);
        game.AddScene(gameScene);
        pause.AddScene(pauseScene);
        manager.Stack.Push(game);
        manager.Stack.Push(pause, new WorldLayerPolicy(true, false, true));
        manager.BeginFrame();
        var step = TimeSpan.FromMilliseconds(10);
        manager.Stack.FixedUpdate(step);
        manager.Stack.FixedUpdate(step);
        manager.Stack.Update(TimeSpan.FromSeconds(3));
        var context = new RecordingDrawContext(TimeSpan.FromSeconds(3), 0.5);
        manager.Stack.Draw(context);
        Assert.Equal(0, gameScene.FixedCount);
        Assert.Equal(0, gameScene.UpdateCount);
        Assert.Equal(0, gameScene.LateCount);
        Assert.Equal(2, pauseScene.FixedCount);
        Assert.Equal(new[] { "game", "pause" }, context.Registrations);
        Assert.Same(context, gameScene.Context);
        Assert.Same(context, pauseScene.Context);
        manager.Stack.Pop();
        manager.BeginFrame();
        manager.Stack.FixedUpdate(step);
        manager.Stack.Update(step);
        Assert.Equal(1, gameScene.FixedCount);
        Assert.Equal(step, gameScene.Elapsed);
        Assert.Equal(1, gameScene.LateCount);
    }

    /// <summary>Queued changes stay deferred across fixed steps and the late phase.</summary>
    [Fact]
    public void FixedStepRequestsWaitUntilTheNextBoundary()
    {
        var manager = new WorldManager();
        World world = manager.CreateWorld();
        var scene = new PhaseScene("world", [])
        {
            DuringFixed = () => manager.Stack.Pop(),
        };
        world.AddScene(scene);
        manager.Stack.Push(world);
        manager.BeginFrame();
        manager.Stack.FixedUpdate(TimeSpan.FromMilliseconds(10));
        manager.Stack.FixedUpdate(TimeSpan.FromMilliseconds(10));
        manager.Stack.Update(TimeSpan.Zero);
        Assert.Equal(2, scene.FixedCount);
        Assert.Equal(1, scene.LateCount);
        Assert.Single(manager.Stack.Worlds);
        manager.BeginFrame();
        Assert.Empty(manager.Stack.Worlds);
    }

    /// <summary>Each new callback rejects reentry and releases its guard after an exception.</summary>
    /// <param name="phase">The callback that throws.</param>
    [Theory]
    [InlineData("fixed")]
    [InlineData("late")]
    [InlineData("draw")]
    public void CallbacksRejectReentryAndRecoverAfterExceptions(string phase)
    {
        var manager = new WorldManager();
        World world = manager.CreateWorld();
        var scene = new PhaseScene("world", []);
        var context = new RecordingDrawContext(TimeSpan.Zero);
        Action fail = () =>
        {
            Assert.Throws<InvalidOperationException>(manager.BeginFrame);
            Assert.Throws<InvalidOperationException>(() => manager.Stack.Update(TimeSpan.Zero));
            Assert.Throws<InvalidOperationException>(() => manager.Stack.FixedUpdate(TimeSpan.FromTicks(1)));
            Assert.Throws<InvalidOperationException>(() => manager.Stack.Draw(context));
            throw new ApplicationException("callback failed");
        };
        scene.DuringFixed = phase == "fixed" ? fail : null;
        scene.AfterUpdate = phase == "late" ? fail : null;
        scene.DuringDraw = phase == "draw" ? fail : null;
        world.AddScene(scene);
        manager.Stack.Push(world);
        manager.BeginFrame();
        Action dispatch = phase switch
        {
            "fixed" => () => manager.Stack.FixedUpdate(TimeSpan.FromTicks(1)),
            "late" => () => manager.Stack.Update(TimeSpan.Zero),
            _ => () => manager.Stack.Draw(context),
        };
        Assert.Throws<ApplicationException>(dispatch);
        scene.DuringFixed = null;
        scene.AfterUpdate = null;
        scene.DuringDraw = null;
        manager.BeginFrame();
        dispatch();
    }

    /// <summary>Hidden lower worlds do not register rendering; simulation can remain active.</summary>
    [Fact]
    public void DrawingPolicyDoesNotBlockSimulation()
    {
        var manager = new WorldManager();
        World lower = manager.CreateWorld();
        World upper = manager.CreateWorld();
        var scene = new PhaseScene("lower", []);
        lower.AddScene(scene);
        upper.AddScene(new PhaseScene("upper", []));
        manager.Stack.Push(lower);
        manager.Stack.Push(upper, new WorldLayerPolicy(false, true, false));
        manager.BeginFrame();
        manager.Stack.FixedUpdate(TimeSpan.FromTicks(1));
        manager.Stack.Update(TimeSpan.Zero);
        var context = new RecordingDrawContext(TimeSpan.Zero);
        manager.Stack.Draw(context);
        Assert.Equal(1, scene.FixedCount);
        Assert.Equal(1, scene.LateCount);
        Assert.Equal(new[] { "upper" }, context.Registrations);
    }

    /// <summary>Invalid timing and contexts fail without entering dispatch.</summary>
    [Fact]
    public void TimingAndContextValidationRejectInvalidValues()
    {
        var manager = new WorldManager();
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Stack.FixedUpdate(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Stack.FixedUpdate(TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.Stack.Update(TimeSpan.FromTicks(-1)));
        Assert.Throws<ArgumentNullException>(() => manager.Stack.Draw(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingDrawContext(TimeSpan.FromTicks(-1)));
        foreach (double alpha in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity, -0.1, 1.1 })
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RecordingDrawContext(TimeSpan.Zero, alpha));
        }

        Assert.Equal(1, new RecordingDrawContext(TimeSpan.Zero).InterpolationAlpha);
        Assert.Equal(0, new RecordingDrawContext(TimeSpan.Zero, 0).InterpolationAlpha);
        manager.BeginFrame();
        manager.Stack.Update(TimeSpan.Zero);
    }

    private sealed class RecordingDrawContext(TimeSpan elapsed, double alpha = 1) : DrawContext(elapsed, alpha)
    {
        public List<string> Registrations { get; } = [];
    }

    private sealed class PhaseScene(string name, List<string> calls) : Scene
    {
        public int FixedCount { get; private set; }

        public int UpdateCount { get; private set; }

        public int LateCount { get; private set; }

        public TimeSpan Elapsed { get; private set; }

        public TimeSpan Step { get; private set; }

        public DrawContext? Context { get; private set; }

        public Action? DuringFixed { get; set; }

        public Action? AfterUpdate { get; set; }

        public Action? DuringDraw { get; set; }

        public override void FixedUpdate(TimeSpan step)
        {
            FixedCount++;
            Step = step;
            DuringFixed?.Invoke();
        }

        public override void Update(TimeSpan elapsed)
        {
            calls.Add(name + ":update");
            UpdateCount++;
            Elapsed = elapsed;
        }

        public override void LateUpdate(TimeSpan elapsed)
        {
            calls.Add(name + ":late");
            LateCount++;
            Elapsed = elapsed;
            AfterUpdate?.Invoke();
        }

        public override void Draw(DrawContext context)
        {
            Context = context;
            ((RecordingDrawContext)context).Registrations.Add(name);
            DuringDraw?.Invoke();
        }
    }
}
