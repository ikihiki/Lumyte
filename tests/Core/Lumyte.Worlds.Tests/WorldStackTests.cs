using Xunit;

namespace Lumyte.Worlds.Tests;

/// <summary>Verifies world lifetime and committed stack behavior.</summary>
public sealed class WorldStackTests
{
    /// <summary>Opening pause stops simulation while retaining the background.</summary>
    [Fact]
    public void PauseAndPopPreserveWorlds()
    {
        var manager = new WorldManager();
        World game = manager.CreateWorld();
        World pause = manager.CreateWorld();
        var calls = new List<string>();
        var gameScene = new Probe("game", calls);
        var pauseScene = new Probe("pause", calls);
        game.AddScene(gameScene);
        pause.AddScene(pauseScene);
        manager.Stack.Push(game);
        Assert.Empty(manager.Stack.Worlds);
        manager.BeginFrame();
        manager.Stack.Push(pause, new WorldLayerPolicy(true, false, true));
        Assert.True(game.ExecutionState.UpdateEnabled);
        manager.BeginFrame();
        Assert.Equal(new WorldExecutionState(false, true, false), game.ExecutionState);
        manager.Stack.HandleInput();
        manager.Stack.Update(TimeSpan.FromMilliseconds(16));
        manager.Stack.Draw();
        Assert.Equal(new[] { "pause:input", "pause:update", "game:draw", "pause:draw" }, calls);
        manager.Stack.Pop();
        manager.BeginFrame();
        Assert.False(pause.IsDestroyed);
        Assert.Equal(default, pause.ExecutionState);
        Assert.True(game.ExecutionState.UpdateEnabled);
        manager.Stack.Push(pause);
        manager.BeginFrame();
        Assert.True(pause.ExecutionState.UpdateEnabled);
    }

    /// <summary>Intermediate stack states do not produce notifications.</summary>
    [Fact]
    public void BatchNotifiesOnlyFinalDifference()
    {
        var manager = new WorldManager();
        World world = manager.CreateWorld();
        var scene = new Probe("world", []);
        world.AddScene(scene);
        manager.Stack.Push(world);
        manager.Stack.Pop();
        manager.BeginFrame();
        Assert.Empty(scene.Changes);
        manager.Stack.Push(world);
        manager.BeginFrame();
        manager.Stack.Pop();
        manager.Stack.Push(world);
        manager.BeginFrame();
        Assert.Single(scene.Changes);
    }

    /// <summary>Notification callbacks queue work for the following boundary.</summary>
    [Fact]
    public void NotificationRequestsAreDeferred()
    {
        var manager = new WorldManager();
        World game = manager.CreateWorld();
        World pause = manager.CreateWorld();
        var scene = new Probe("game", [])
        {
            Changed = () => manager.Stack.Push(pause, new WorldLayerPolicy(true, false, true)),
        };
        game.AddScene(scene);
        manager.Stack.Push(game);
        manager.BeginFrame();
        Assert.Single(manager.Stack.Worlds);
        scene.Changed = null;
        manager.BeginFrame();
        Assert.Equal(2, manager.Stack.Worlds.Count);
        Assert.False(game.ExecutionState.UpdateEnabled);
    }

    /// <summary>Destruction removes references and notifies before detaching scenes.</summary>
    [Fact]
    public void DestroyRemovesStackReferencesAtBoundary()
    {
        var manager = new WorldManager();
        World world = manager.CreateWorld();
        var scene = new Probe("world", []);
        world.AddScene(scene);
        manager.Stack.Push(world);
        manager.BeginFrame();
        scene.Changed = () =>
        {
            Assert.False(world.IsDestroyed);
            Assert.Same(world, scene.World);
            Assert.Empty(manager.Stack.Worlds);
        };
        manager.RequestDestroy(world);
        manager.RequestDestroy(world);
        Assert.False(world.IsDestroyed);
        manager.BeginFrame();
        Assert.True(world.IsDestroyed);
        Assert.Null(scene.World);
        Assert.Empty(manager.Worlds);
        Assert.Throws<ObjectDisposedException>(() => manager.Stack.Push(world));
    }

    /// <summary>Invalid batches are atomic and destruction remains pending.</summary>
    [Fact]
    public void InvalidBatchLeavesCommittedStateIntact()
    {
        var manager = new WorldManager();
        World world = manager.CreateWorld();
        manager.Stack.Push(world);
        manager.BeginFrame();
        manager.Stack.Push(world);
        manager.RequestDestroy(world);
        Assert.Throws<InvalidOperationException>(manager.BeginFrame);
        Assert.Single(manager.Stack.Worlds);
        Assert.True(world.ExecutionState.UpdateEnabled);
        manager.BeginFrame();
        Assert.True(world.IsDestroyed);
    }

    /// <summary>One notification failure cannot skip other notifications or destruction.</summary>
    [Fact]
    public void NotificationErrorsAreAggregatedAfterDestruction()
    {
        var manager = new WorldManager();
        World world = manager.CreateWorld();
        var first = new Probe("first", []);
        var second = new Probe("second", []);
        world.AddScene(first);
        world.AddScene(second);
        manager.Stack.Push(world);
        manager.BeginFrame();
        first.Changed = () => throw new InvalidOperationException("callback");
        manager.RequestDestroy(world);
        AggregateException error = Assert.Throws<AggregateException>(manager.BeginFrame);
        Assert.Single(error.InnerExceptions);
        Assert.Equal(2, second.Changes.Count);
        Assert.True(world.IsDestroyed);
        manager.BeginFrame();
    }

    /// <summary>Each policy axis independently blocks all lower layers.</summary>
    /// <param name="update">Whether updates are blocked.</param>
    /// <param name="draw">Whether drawing is blocked.</param>
    /// <param name="input">Whether input is blocked.</param>
    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void BlockingIsIndependent(bool update, bool draw, bool input)
    {
        var manager = new WorldManager();
        World lower = manager.CreateWorld();
        World middle = manager.CreateWorld();
        World upper = manager.CreateWorld();
        manager.Stack.Push(lower);
        manager.Stack.Push(middle, new WorldLayerPolicy(update, draw, input));
        manager.Stack.Push(upper);
        manager.BeginFrame();
        Assert.Equal(new WorldExecutionState(!update, !draw, !input), lower.ExecutionState);
        Assert.Equal(new WorldExecutionState(true, true, true), middle.ExecutionState);
        Assert.Equal(middle.ExecutionState, upper.ExecutionState);
    }

    /// <summary>Dispatch order and consumption are independent of queued mutations.</summary>
    [Fact]
    public void DispatchUsesCommittedStackAndRejectsBoundaryReentry()
    {
        var manager = new WorldManager();
        var calls = new List<string>();
        World lower = manager.CreateWorld();
        World upper = manager.CreateWorld();
        lower.AddScene(new Probe("lower", calls));
        var top = new Probe("upper", calls)
        {
            Input = () =>
            {
                manager.Stack.Pop();
                Assert.Throws<InvalidOperationException>(manager.BeginFrame);
                return true;
            },
        };
        upper.AddScene(top);
        manager.Stack.Replace(lower);
        manager.Stack.Push(upper);
        manager.BeginFrame();
        manager.Stack.HandleInput();
        manager.Stack.Update(TimeSpan.Zero);
        Assert.Equal(new[] { "upper:input", "lower:update", "upper:update" }, calls);
        manager.BeginFrame();
        Assert.Single(manager.Stack.Worlds);
    }

    /// <summary>Scenes have one owner and active worlds cannot be edited.</summary>
    [Fact]
    public void OwnershipAndInactiveEditingAreEnforced()
    {
        var manager = new WorldManager();
        var other = new WorldManager();
        World world = manager.CreateWorld();
        var scene = new Probe("scene", []);
        world.AddScene(scene);
        Assert.Throws<InvalidOperationException>(() => other.CreateWorld().AddScene(scene));
        Assert.Throws<ArgumentException>(() => other.Stack.Push(world));
        manager.Stack.Push(world);
        manager.BeginFrame();
        Assert.Throws<InvalidOperationException>(() => world.RemoveScene(scene));
        manager.Stack.Pop();
        manager.BeginFrame();
        Assert.True(world.RemoveScene(scene));
        Assert.Null(scene.World);
        manager.Stack.Pop();
        manager.BeginFrame();
        Assert.Empty(manager.Stack.Worlds);
    }

    private sealed class Probe(string name, List<string> calls) : Scene
    {
        public List<WorldExecutionState> Changes { get; } = [];

        public Action? Changed { get; set; }

        public Func<bool>? Input { get; set; }

        public override void OnExecutionStateChanged(WorldExecutionState previous, WorldExecutionState current)
        {
            Changes.Add(current);
            Changed?.Invoke();
        }

        public override void Update(TimeSpan elapsed) => calls.Add(name + ":update");

        public override void Draw() => calls.Add(name + ":draw");

        public override bool HandleInput()
        {
            calls.Add(name + ":input");
            return Input?.Invoke() ?? false;
        }
    }
}
