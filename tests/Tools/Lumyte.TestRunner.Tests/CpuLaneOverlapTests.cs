using Xunit;

// Exercise the reviewed CPU-only override of a native-oriented assembly boundary.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
[assembly: System.Reflection.AssemblyMetadata("Lumyte.CpuMethodParallelism", "true")]

namespace Lumyte.TestRunner.Tests;

/// <summary>Single-threaded CPU payload; overlap itself is proved by the integration runner fixture.</summary>
public sealed class CpuLaneOverlapTests
{
    /// <summary>A pure payload for the shared CPU scheduling adapter.</summary>
    [Fact]
    public void FirstMethod() => Assert.Equal(4, 2 + 2);

    /// <summary>A second pure payload in the same class.</summary>
    [Fact]
    public void SecondMethod() => Assert.Equal(9, 3 * 3);
}
