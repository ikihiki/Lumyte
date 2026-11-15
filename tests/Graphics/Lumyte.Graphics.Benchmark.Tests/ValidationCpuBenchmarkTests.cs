using Lumyte.Graphics.Validation.Benchmark.Transfers;
using Xunit;

namespace Lumyte.Graphics.Benchmark.Tests;

/// <summary>Checks equivalent consumed operations with and without production validation decoration.</summary>
public sealed class ValidationCpuBenchmarkTests
{
    /// <summary>Repeats bounded transfers and recording with balanced resource ownership.</summary>
    /// <param name="validated">Whether the production wrapper is selected.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransfersAreRealAndRepeatable(bool validated)
    {
        var workload = new TransferBenchmarks { Validated = validated, ElementCount = 16 };
        workload.Setup();
        try
        {
            for (int wave = 0; wave < 3; wave++)
            {
                Assert.NotEqual(0UL, workload.MapCopyUnmap());
                Assert.Equal(64, workload.Counters.UploadedBytes);
                Assert.Equal(1, workload.CopyRecordFinishRetire());
                Assert.Equal(1, workload.Counters.Copies);
                Assert.Equal(3, workload.Counters.LiveResources);
            }
        }
        finally
        {
            workload.Cleanup();
        }

        Assert.Equal(0, workload.Counters.LiveResources);
    }
}
