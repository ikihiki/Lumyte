namespace Lumyte.StateMachines.Benchmark;

/// <summary>Deterministic checks outside all timing boundaries.</summary>
public static class BenchmarkChecks
{
    /// <summary>Verifies every fixture profile and independent selection and freeze invariants.</summary>
    public static void Verify()
    {
        foreach (int count in new[] { 1, 8, 128 })
        {
            foreach (bool priority in new[] { false, true })
            {
                var benchmark = new StateMachinesBenchmarks { States = count, MixedPriority = priority };
                benchmark.Setup();
                Require(benchmark.RegisterAndFreeze() is not null);
                Require(benchmark.ComposeDefinition() is not null);
                Require(benchmark.CreateInstance().Context[1] == 1);
                Require(!benchmark.CanFire());
                Require(benchmark.CanFire());
                Require(benchmark.CanFireAny());
                for (int i = 0; i < count * 2; i++)
                {
                    Require(benchmark.Fire());
                    Require(benchmark.FireAny());
                    Require(benchmark.SelfTransition());
                }
            }
        }

        foreach (int subscribers in new[] { 1, 8 })
        {
            var benchmark = new StateMachinesBenchmarks { States = 8, Subscribers = subscribers };
            benchmark.Setup();
            Require(benchmark.Fire());
            Require(benchmark.FireAny());
        }

        foreach (string phase in new[] { "guard", "exit", "effect", "enter", "notification", "diagnostic" })
        {
            var benchmark = new StateFailureBenchmarks { Phase = phase };
            benchmark.Setup();
            try
            {
                Require(benchmark.Recover());
                Require(benchmark.Recover());
                Require(benchmark.CompletedCount == 2);
                Require(benchmark.FailureCount == (phase == "diagnostic" ? 0 : 2));
                Require(benchmark.SampledCount == (phase == "diagnostic" ? 2 : 0));
                Require(benchmark.MatchedActivityCount == (phase == "diagnostic" ? 2 : 0));
            }
            finally
            {
                benchmark.Cleanup();
            }
        }

        foreach (int count in new[] { 8, 128 })
        {
            foreach (string match in new[] { "first", "last", "rejected", "unknown" })
            {
                var benchmark = new GuardSelectionBenchmarks { Candidates = count, Match = match };
                benchmark.Setup();
                Require(benchmark.Select() == (match is "first" or "last"));
            }
        }

        var a = new State<int[]>("a");
        var b = new State<int[]>("b");
        var c = new State<int[]>("c");
        var first = new Transition<int[], int>(a, b, 1);
        Transition<int[], int> best = new Transition<int[], int>(a, c, 2).WithPriority(10);
        var builder = new StateMachineBuilder<int[], int>(a);
        builder.AddTransition(first);
        builder.AddTransition(best);
        StateMachine<int[], int> definition = builder.BuildDefinition();
        var instance = new StateMachineInstance<int[], int>(definition, []);
        Require(ReferenceEquals(instance.CurrentState, a));
        Require(!instance.CanFireAny([]));
        Require(instance.FireAny([1, 1, 2]));
        Require(ReferenceEquals(instance.CurrentState, c));
        Require(ReferenceEquals(new StateMachineInstance<int[], int>(definition, []).CurrentState, a));
        try
        {
            first.WithPriority(99);
            throw new InvalidOperationException("A frozen transition was mutable.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "A compiled transition cannot be changed.")
        {
        }
    }

    private static void Require(bool condition)
    {
        if (!condition)
        {
            throw new InvalidOperationException("State machine benchmark invariant failed.");
        }
    }
}
