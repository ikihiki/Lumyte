using Lumyte.Graphics.Benchmark.Integration;
using Xunit;

namespace Lumyte.Graphics.Benchmark.Tests;

/// <summary>Opt-in real-hardware pixel and bounded frame-lifetime checks, never timing assertions.</summary>
public sealed class GpuIntegratedBenchmarkTests
{
    /// <summary>Verifies all twelve expanded benchmark cases and repeated slot reuse against analytic final pixels.</summary>
    /// <returns>Completion of the untimed hardware checks.</returns>
    [IntegrationGpuFact]
    public async Task RectangleFramesCompleteAndMatchAnalyticPixelsAsync()
    {
        foreach (int count in new[] { 1024, 10000 })
        {
            foreach (RectangleInputProfile profile in Enum.GetValues<RectangleInputProfile>())
            {
                foreach (int depth in new[] { 1, 3 })
                {
                    await using var workload = new RectangleFrameWorkload(count, profile, depth);
                    Assert.Contains("Radeon 8060S", workload.Identity, StringComparison.Ordinal);
                    for (int operation = 0; operation < 2; operation++)
                    {
                        await workload.RunAsync();
                        Assert.Equal(6, workload.CompletedFrames);
                        Assert.Equal(5, workload.LastCompletedFrame);
                        Assert.Equal(depth, workload.PeakOutstanding);
                        await workload.VerifyLastFrameAsync();
                    }
                }
            }
        }
    }

    /// <summary>Reports a genuine xUnit skip unless real-GPU execution is explicitly requested.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class IntegrationGpuFactAttribute : FactAttribute
    {
        /// <summary>Initializes a new instance of the <see cref="IntegrationGpuFactAttribute"/> class.</summary>
        public IntegrationGpuFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_INTEGRATION_GPU") != "1")
            {
                Skip = "Requires LUMYTE_GRAPHICS_INTEGRATION_GPU=1 and the explicit Radeon hardware manifest; no software fallback.";
            }
        }
    }
}
