using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Exercises finite dense window admission without creating a native device.</summary>
public sealed class GenericWindowPlanningTests
{
    /// <summary>Dense paint may exceed a registered range without reducing any target samples.</summary>
    [Fact]
    public void EverySampleIsCoveredExactlyOnceAt64KiB()
    {
        PagedGpuPreparation2D.SampleWindow[] windows = PagedGpuPreparation2D.Admit(Caps(), 128 * 128 * 2 * 16, 128, new()).Windows;
        Assert.Equal(128, windows.Length);
        uint next = 0;
        foreach (PagedGpuPreparation2D.SampleWindow window in windows)
        {
            Assert.Equal(next, window.Offset);
            Assert.Equal(4096u, window.Count);
            Assert.Equal(0u, window.Offset % 16);
            next += window.Count;
        }

        Assert.Equal(128u * 128 * 2 * 16, next);
    }

    /// <summary>Offset alignment and the last partial window preserve complete correlated pixels.</summary>
    [Fact]
    public void AlignmentAndFinalPixelWindowAreExplicit()
    {
        DeviceCaps caps = Caps() with { StorageBufferOffsetAlignment = 768 };
        PagedGpuPreparation2D.SampleWindow[] windows = PagedGpuPreparation2D.Admit(caps, 4112, 128, new()).Windows;
        Assert.Equal(2, windows.Length);
        Assert.Equal(3840u, windows[0].Count);
        Assert.Equal(272u, windows[1].Count);
        Assert.Equal(0u, (windows[1].Offset * 4) % 768);
        Assert.Equal(0u, (windows[1].Offset * 16) % 768);
    }

    /// <summary>Arbitrary-read scenes and unbounded window inventories reject before allocation.</summary>
    [Fact]
    public void SceneAndAggregateCeilingsRejectRatherThanTruncate()
    {
        Assert.Throws<NotSupportedException>(() => PagedGpuPreparation2D.Admit(Caps(), 16, 12289, new()));
        Assert.Throws<NotSupportedException>(() => PagedGpuPreparation2D.Admit(Caps(), 4096L * 513, 128, new()));
        Assert.Throws<NotSupportedException>(() => PagedGpuPreparation2D.Admit(Caps() with { MaxStorageBuffersPerStage = 4 }, 16, 128, new()));
        Assert.Throws<NotSupportedException>(() => PagedGpuPreparation2D.Admit(Caps() with { MaxStorageBufferBindingSize = 128 }, 16, 1, new()));
    }

    /// <summary>Three source pages consume exactly seven worst-case physical paint buffers, including the remap.</summary>
    [Fact]
    public void ThreePageInventoryAndExplicitBudgetsAreFinite()
    {
        Assert.Equal(3, PagedGpuPreparation2D.Admit(Caps(), 16, 12288, new()).ScenePages);
        Assert.Throws<NotSupportedException>(() => PagedGpuPreparation2D.Admit(Caps() with { MaxStorageBuffersPerStage = 6 }, 16, 12288, new()));
        Assert.Throws<NotSupportedException>(() => PagedGpuPreparation2D.Admit(Caps(), 16, 8193, new(maxScenePages: 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenericPagingLimits2D(maxWindows: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenericPagingLimits2D(maxWindows: 4097));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GenericPagingLimits2D(maxScenePages: 4));
    }

    private static DeviceCaps Caps() => new()
    {
        MaxBufferSize = 1UL << 30,
        MaxStorageBufferBindingSize = 65536,
        MaxStorageBuffersPerStage = 7,
        MaxComputeWorkgroupsPerDimension = 65535,
        MaxComputeWorkgroupSizeX = 64,
        MaxComputeInvocationsPerWorkgroup = 64,
        ArgumentTableBindingLimits = new() { BufferSlots = 7, BuffersPerDraw = 7, MaximumBufferRangeBytes = 65536 },
    };
}
