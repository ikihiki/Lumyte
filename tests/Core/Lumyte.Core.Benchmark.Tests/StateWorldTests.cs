using System.Diagnostics;
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

    /// <summary>Overlapping listeners attribute identical transitions only to their own fixture calls.</summary>
    [Fact]
    public void OverlappingDiagnosticFixturesCountOnlyOwnCalls()
    {
        var first = new StateMachines.Benchmark.Machines.StateFailureBenchmarks { Phase = "diagnostic" };
        var second = new StateMachines.Benchmark.Machines.StateFailureBenchmarks { Phase = "diagnostic" };
        first.Setup();
        second.Setup();
        try
        {
            Assert.True(first.Recover());
            Assert.True(second.Recover());
            Assert.Equal(new[] { 1, 1 }, new[] { first.CompletedCount, second.CompletedCount });
            Assert.Equal(new[] { 1, 1, 1, 1 }, new[] { first.SampledCount, first.MatchedActivityCount, second.SampledCount, second.MatchedActivityCount });
            first.Setup();
            Assert.True(first.Recover());
            Assert.Equal(new[] { 1, 1, 1, 1 }, new[] { first.SampledCount, first.MatchedActivityCount, second.SampledCount, second.MatchedActivityCount });
            first.Cleanup();
            Assert.True(second.Recover());
            Assert.True(first.Recover());
            Assert.Equal(new[] { 1, 1, 2, 2 }, new[] { first.SampledCount, first.MatchedActivityCount, second.SampledCount, second.MatchedActivityCount });
        }
        finally
        {
            first.Cleanup();
            second.Cleanup();
        }
    }

    /// <summary>Real unrelated activities remain observable without polluting fixture sampling or matching counts.</summary>
    [Fact]
    public void UnrelatedMachinesDoNotPolluteDiagnosticCounts()
    {
        int owner = Environment.CurrentManagedThreadId;
        int stopped = 0;
        using var observer = new ActivityListener
        {
            ShouldListenTo = source => source.Name == StateMachineDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = _ =>
            {
                if (Environment.CurrentManagedThreadId == owner)
                {
                    stopped++;
                }
            },
        };
        ActivitySource.AddActivityListener(observer);
        var benchmark = new StateMachines.Benchmark.Machines.StateFailureBenchmarks { Phase = "diagnostic" };
        benchmark.Setup();
        try
        {
            FireDiagnosticNoise("noise", 7);
            FireDiagnosticNoise("self", 0);
            Assert.True(benchmark.Recover());
            Assert.Equal(3, stopped);
            Assert.Equal(1, benchmark.CompletedCount);
            Assert.Equal(new[] { 1, 1 }, new[] { benchmark.SampledCount, benchmark.MatchedActivityCount });
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Nested fixture calls and matching-tag noise stay independent even with an earlier observer.</summary>
    [Fact]
    public void NestedDiagnosticFixturesKeepIndependentObservations()
    {
        int owner = Environment.CurrentManagedThreadId;
        bool nest = false;
        var outer = new StateMachines.Benchmark.Machines.StateFailureBenchmarks { Phase = "diagnostic" };
        var inner = new StateMachines.Benchmark.Machines.StateFailureBenchmarks { Phase = "diagnostic" };
        using var observer = new ActivityListener
        {
            ShouldListenTo = source => source.Name == StateMachineDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = _ =>
            {
                if (Environment.CurrentManagedThreadId == owner && nest)
                {
                    nest = false;
                    Assert.True(inner.Recover());
                    FireDiagnosticNoise("self", 0);
                }
            },
        };
        ActivitySource.AddActivityListener(observer);
        outer.Setup();
        inner.Setup();
        try
        {
            nest = true;
            Assert.True(outer.Recover());
            Assert.False(nest);
            Assert.Equal(new[] { 1, 1 }, new[] { outer.CompletedCount, inner.CompletedCount });
            Assert.Equal(new[] { 1, 1, 1, 1 }, new[] { outer.SampledCount, outer.MatchedActivityCount, inner.SampledCount, inner.MatchedActivityCount });
        }
        finally
        {
            outer.Cleanup();
            inner.Cleanup();
        }
    }

    /// <summary>Every eligible same-parent sibling is sampled instead of capping a call at one observation.</summary>
    [Fact]
    public void EligibleSiblingActivityIsNotSuppressed()
    {
        int owner = Environment.CurrentManagedThreadId;
        Activity? parent = Activity.Current;
        bool emitSibling = false;
        using var observer = new ActivityListener
        {
            ShouldListenTo = source => source.Name == StateMachineDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                if (Environment.CurrentManagedThreadId != owner || !emitSibling || activity.OperationName != "StateMachine.Fire")
                {
                    return;
                }

                emitSibling = false;
                Activity? previous = Activity.Current;
                Activity.Current = parent;
                try
                {
                    using Activity? sibling = StateMachineDiagnostics.Activities.StartActivity("StateMachine.Fire", ActivityKind.Internal);
                    Assert.NotNull(sibling);
                    Assert.Equal(parent?.Id, sibling.ParentId);
                    sibling.SetTag("state_machine.transitioned", true);
                    sibling.SetTag("state_machine.target", "self");
                    sibling.SetTag("state_machine.trigger", "0");
                }
                finally
                {
                    Activity.Current = previous;
                }
            },
        };
        ActivitySource.AddActivityListener(observer);
        var benchmark = new StateMachines.Benchmark.Machines.StateFailureBenchmarks { Phase = "diagnostic" };
        benchmark.Setup();
        try
        {
            emitSibling = true;
            Assert.True(benchmark.Recover());
            Assert.False(emitSibling);
            Assert.Same(parent, Activity.Current);
            Assert.Equal(1, benchmark.CompletedCount);
            Assert.Equal(2, benchmark.SampledCount);
            Assert.Equal(1, benchmark.MatchedActivityCount);
        }
        finally
        {
            benchmark.Cleanup();
        }
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

    private static void FireDiagnosticNoise(string name, int trigger)
    {
        var state = new State<int>(name);
        var builder = new StateMachineBuilder<int, int>(state);
        builder.AddTransition(new Transition<int, int>(state, state, trigger));
        Assert.True(builder.Build(0).Fire(trigger));
    }
}
