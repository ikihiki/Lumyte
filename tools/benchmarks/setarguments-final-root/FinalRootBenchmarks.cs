using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.FinalRoot.Benchmarks;

// One invocation = one fresh command buffer, Count selections/dispatches,
// finish + submit + actual completion wait + submission/command disposal.
public class FinalRootBenchmarks
{
    private FinalRootFixture _fixture = null!;
    [Params(1, 64)] public int Count { get; set; }

    [GlobalSetup]
    public async Task Setup() => _fixture = await FinalRootFixture.CreateAsync(Count);

    [Benchmark]
    public Task InterfaceSetDispatchComplete() => _fixture.InterfaceSetDispatchComplete();

    [Benchmark]
    public Task ExplicitSnapshotSetDispatchComplete() => _fixture.ExplicitSnapshotSetDispatchComplete();

    internal Task VerifyInterfaceAsync() => _fixture.VerifyAsync(snapshot: false);
    internal Task VerifySnapshotsAsync() => _fixture.VerifyAsync(snapshot: true);

    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();
}
