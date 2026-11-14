using BenchmarkDotNet.Attributes;

namespace Lumyte.Graphics.UploadStrategies.Benchmarks;

public class CpuBenchmarks
{
    private TransferFixture _fixture = null!;
    [Params(1, 64)] public int Count { get; set; }

    [GlobalSetup]
    public async Task Setup()
    {
        _fixture = await TransferFixture.CreateAsync(Count);
        await _fixture.VerifyAsync();
        await _fixture.MapForCpuAsync();
    }

    [Benchmark]
    public int MappedReferenceWrite() => _fixture.MappedReferenceWrite();

    internal async Task VerifyLastWriteAsync()
    {
        _fixture.UnmapForCpu();
        await _fixture.VerifyAsync(preserveReferenceUpload: true);
        await _fixture.MapForCpuAsync();
    }

    [GlobalCleanup]
    public void Cleanup() => _fixture.Dispose();
}
