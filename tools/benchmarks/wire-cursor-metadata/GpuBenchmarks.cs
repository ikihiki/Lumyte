using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.UploadStrategies.Benchmarks;

// Host wall latency through actual GPU completion, not a device timestamp.
public class GpuBenchmarks
{
    private TransferFixture _fixture = null!;
    [Params(1, 64)] public int Count { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _fixture = await TransferFixture.CreateAsync(Count);
        await _fixture.VerifyAsync();
    }

    [Benchmark]
    public Task ReferenceWriteCopyComplete() => _fixture.ReferenceWriteCopyComplete();

    internal Task VerifyLastWriteAsync() =>
        _fixture.VerifyAsync(preserveReferenceUpload: true, referenceAlreadyCopied: true);

    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();
}
