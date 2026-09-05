using BenchmarkDotNet.Attributes;
using Lumyte.Interaction;

namespace Lumyte.Benchmarks;

[MemoryDiagnoser]
public class InteractionResolverBenchmarks
{
    private readonly InteractionContext context = new();
    private readonly InteractionResolver resolver = new();
    private InteractionCandidate<int>[] candidates = null!;

    [Params(1, 8, 32)]
    public int CandidateCount { get; set; }

    [GlobalSetup]
    public void Setup() => candidates =
    [
        .. Enumerable.Range(0, CandidateCount).Select(index =>
            new InteractionCandidate<int>(index)
            {
                Priority = index,
                When = index % 3 == 0 ? ContextCondition.Never : ContextCondition.Always,
            }),
    ];

    [Benchmark]
    public InteractionResolution<int> Resolve() => resolver.Resolve(candidates, context);
}
