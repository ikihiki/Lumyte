using Lumyte.StateMachines;
using Lumyte.Worlds;
using Xunit;

namespace Lumyte.Core.Benchmark.Tests;

/// <summary>Tests benchmark fixture oracles and independent public API boundaries.</summary>
public sealed class StateWorldTests
{
    /// <summary>All state benchmark fixtures preserve their deterministic invariants.</summary>
    [Fact]
    public void StateFixturesVerify() => StateMachines.Benchmark.BenchmarkChecks.Verify();

    /// <summary>All CPU world benchmark fixtures preserve their deterministic invariants.</summary>
    [Fact]
    public void WorldFixturesVerify() => Worlds.Benchmark.BenchmarkChecks.Verify();

    /// <summary>Each configured failure must actually throw before exactly one successful recovery.</summary>
    /// <param name="phase">The failing callback.</param>
    [Theory]
    [InlineData("guard")]
    [InlineData("exit")]
    [InlineData("effect")]
    [InlineData("enter")]
    [InlineData("notification")]
    public void ExpectedFailureIsObservedBeforeRecovery(string phase)
    {
        var benchmark = new StateMachines.Benchmark.Machines.StateFailureBenchmarks { Phase = phase };
        benchmark.Setup();
        Assert.True(benchmark.Recover());
        Assert.Equal(1, benchmark.FailureCount);
        Assert.Equal(1, benchmark.CompletedCount);
        Assert.True(benchmark.Recover());
        Assert.Equal(2, benchmark.FailureCount);
        Assert.Equal(2, benchmark.CompletedCount);
    }

    /// <summary>Diagnostic success must produce sampled and stopped activities with transition tags.</summary>
    [Fact]
    public void DiagnosticTransitionEmitsMatchingActivities()
    {
        var benchmark = new StateMachines.Benchmark.Machines.StateFailureBenchmarks { Phase = "diagnostic" };
        benchmark.Setup();
        try
        {
            Assert.True(benchmark.Recover());
            Assert.Equal(0, benchmark.FailureCount);
            Assert.Equal(1, benchmark.CompletedCount);
            Assert.Equal(1, benchmark.SampledCount);
            Assert.Equal(1, benchmark.MatchedActivityCount);
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>An absent expected failure cannot masquerade as a successful recovery.</summary>
    [Fact]
    public void MissingFailureCannotPass()
    {
        var benchmark = new StateMachines.Benchmark.Machines.StateFailureBenchmarks { Phase = "diagnostic" };
        benchmark.Setup();
        try
        {
            benchmark.Phase = "guard";
            Assert.False(benchmark.Recover());
            Assert.Equal(0, benchmark.FailureCount);
        }
        finally
        {
            benchmark.Cleanup();
        }

        benchmark = new StateMachines.Benchmark.Machines.StateFailureBenchmarks { Phase = "guard" };
        benchmark.Setup();
        benchmark.Phase = "diagnostic";
        Assert.True(benchmark.Recover());
        Assert.Equal(0, benchmark.SampledCount);
        Assert.Equal(0, benchmark.MatchedActivityCount);
        Assert.Throws<ArgumentException>(() => new StateMachines.Benchmark.Machines.StateFailureBenchmarks { Phase = "invalid" }.Setup());
    }

    /// <summary>Trigger input order cannot override transition priority or affect other instances.</summary>
    [Fact]
    public void PriorityWinsOverInputOrder()
    {
        var first = new State<int>("first");
        var second = new State<int>("second");
        var third = new State<int>("third");
        var builder = new StateMachineBuilder<int, string>(first);
        builder.AddTransition(new Transition<int, string>(first, second, "low"));
        builder.AddTransition(new Transition<int, string>(first, third, "high").WithPriority(10));
        StateMachine<int, string> definition = builder.BuildDefinition();
        var machine = new StateMachineInstance<int, string>(definition, 0);
        Assert.False(machine.CanFireAny([]));
        Assert.True(machine.FireAny(["low", "low", "high"]));
        Assert.Same(third, machine.CurrentState);
        Assert.Same(first, new StateMachineInstance<int, string>(definition, 0).CurrentState);
    }

    /// <summary>Duplicate destruction stays deferred and removes both manager and stack references.</summary>
    [Fact]
    public void DestructionIsDeferredAndIdempotent()
    {
        var manager = new WorldManager();
        World world = manager.CreateWorld();
        manager.Stack.Push(world);
        Assert.Empty(manager.Stack.Worlds);
        manager.BeginFrame();
        manager.RequestDestroy(world);
        manager.RequestDestroy(world);
        Assert.False(world.IsDestroyed);
        Assert.Single(manager.Stack.Worlds);
        manager.BeginFrame();
        Assert.True(world.IsDestroyed);
        Assert.Empty(manager.Worlds);
        Assert.Empty(manager.Stack.Worlds);
        Assert.Throws<ObjectDisposedException>(() => manager.RequestDestroy(world));
    }
}
