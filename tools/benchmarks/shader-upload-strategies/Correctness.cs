using System.Text.Json;

namespace Lumyte.Graphics.UploadStrategies.Benchmarks;

public static class Correctness
{
    public static async Task<string> RunAsync()
    {
        var checks = new List<object>();
        foreach (int count in new[] { 1, 64, 1024 })
        {
            using TransferFixture fixture = await TransferFixture.CreateAsync(count);
            await fixture.VerifyAsync();
            await fixture.MapForCpuAsync();
            TransferFixture.Require(fixture.MappedReferenceWrite() == 2, "CPU metadata count differs.");
            TransferFixture.Require(fixture.MappedReferenceWrite() == 2, "Repeated CPU metadata count differs.");
            fixture.UnmapForCpu();
            await fixture.VerifyAsync(preserveReferenceUpload: true);
            await fixture.ReferenceWriteCopyComplete();
            await fixture.VerifyAsync(preserveReferenceUpload: true, referenceAlreadyCopied: true);
            checks.Add(new { count, passed = true, referenceCompiledStride = fixture.ReferenceStride,
                numericFields = true, all16MatrixFields = true, scalarAndVector = true,
                vector3ArrayStride = true, realReadOnlyAndReadWriteReferences = true,
                referenceOnlyMetadata = true, gpuComputeAndReadback = true });
        }
        return JsonSerializer.Serialize(new { passed = true,
            contract = "same generated classes, actual typed upload CopyFrom and immutable reference-only metadata",
            cpuBoundary = "update preallocated objects, CopyFrom while persistently mapped, one terminal metadata count read",
            gpuBoundary = "update/map/CopyFrom/unmap/new commands/barriers/copy/finish/submit/wait/retire; host wall latency",
            checks });
    }
}
