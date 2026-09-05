using BenchmarkDotNet.Attributes;
using Lumyte.StateMachine;

using static Lumyte.StateMachine.StateMachineKit;

namespace Lumyte.Benchmarks;

[MemoryDiagnoser]
public class StateMachineBenchmarks
{
    private StateMachineInstance<BenchmarkContext, BenchmarkTrigger> machine = null!;

    [Params(1, 8, 32)]
    public int CandidateCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        State<BenchmarkContext> idle = State<BenchmarkContext>("Idle");
        State<BenchmarkContext> active = State<BenchmarkContext>("Active");
        Transition<BenchmarkContext, BenchmarkTrigger>[] transitions =
        [
            .. Enumerable.Range(0, CandidateCount).Select(index =>
                Transition(idle, active, BenchmarkTrigger.Activate)
                    .When(context => context.Selection == index)),
            Transition(active, idle, BenchmarkTrigger.Reset),
        ];
        StateMachine<BenchmarkContext, BenchmarkTrigger> definition =
            Machine<BenchmarkContext, BenchmarkTrigger>(idle)[transitions];
        machine = definition.CreateInstance(new() { Selection = CandidateCount - 1 });
    }

    [Benchmark]
    public bool FireAndReset() =>
        machine.Fire(BenchmarkTrigger.Activate)
        & machine.Fire(BenchmarkTrigger.Reset);

    public sealed class BenchmarkContext
    {
        public int Selection { get; init; }
    }

    public enum BenchmarkTrigger
    {
        Activate,
        Reset,
    }
}
