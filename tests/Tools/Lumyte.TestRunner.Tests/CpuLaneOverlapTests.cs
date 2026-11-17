using Xunit;

// Exercise the reviewed CPU-only override of a native-oriented assembly boundary.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: System.Reflection.AssemblyMetadata("Lumyte.CpuMethodParallelism", "true")]

namespace Lumyte.TestRunner.Tests;

/// <summary>Proves shared MSBuild wiring actually overlaps two methods in the same class when opted in.</summary>
public sealed class CpuLaneOverlapTests
{
    private static readonly Barrier _barrier = new(2);

    /// <summary>Meets the other method only when the CPU lane has at least two method slots.</summary>
    [Fact]
    public void FirstMethod() => MeetOtherMethod();

    /// <summary>Meets the other method only when the CPU lane has at least two method slots.</summary>
    [Fact]
    public void SecondMethod() => MeetOtherMethod();

    private static void MeetOtherMethod()
    {
        if (int.TryParse(Environment.GetEnvironmentVariable("LUMYTE_CPU_METHOD_PARALLELISM"), out int concurrency) && concurrency >= 2)
        {
            Assert.True(_barrier.SignalAndWait(TimeSpan.FromSeconds(15)), "CPU lane did not run same-class methods concurrently.");
        }
    }
}
