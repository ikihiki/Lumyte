using System.Runtime.InteropServices;

using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

[Collection("GpuBackend")]
public sealed class DirectX12NativeMemoryTests
{
    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void PlacedRegionsMapTheirOwnBytes()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuMemoryRequirements requirements = backend.GetLinearMemoryRequirements(
            64, NativeGpuMemoryKind.CpuVisible);
        NativeGpuHeap heap = backend.CreateGpuHeap(
            checked(requirements.Size * 3), requirements.Alignment,
            NativeGpuMemoryKind.CpuVisible, [requirements.Compatibility]);
        try
        {
            NativeGpuLinearRegion first = backend.CreateLinearRegion(64, heap, requirements.Size);
            try
            {
                NativeGpuLinearRegion second = backend.CreateLinearRegion(64, heap, checked(requirements.Size * 2));
                try
                {
                    byte[] expectedFirst = [11, 29, 47, 83, 101];
                    byte[] expectedSecond = [239, 197, 163, 131, 109];

                    Marshal.Copy(expectedFirst, 0, first.CpuAddress + 7, expectedFirst.Length);
                    Marshal.Copy(expectedSecond, 0, second.CpuAddress + 3, expectedSecond.Length);

                    Assert.Collection(
                        new[] { Read(first.CpuAddress + 7, 5), Read(second.CpuAddress + 3, 5) },
                        actual => Assert.Equal(expectedFirst, actual),
                        actual => Assert.Equal(expectedSecond, actual));
                }
                finally { backend.DestroyLinearRegion(second); }
            }
            finally { backend.DestroyLinearRegion(first); }
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void RangeAddressesAreRelativeToThePlacedRegion()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuMemoryRequirements requirements = backend.GetLinearMemoryRequirements(
            64, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap heap = backend.CreateGpuHeap(
            checked(requirements.Size * 2), requirements.Alignment,
            NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        try
        {
            NativeGpuLinearRegion region = backend.CreateLinearRegion(64, heap, requirements.Size);
            try
            {
                var range = new NativeGpuRange(region, 8, 32);

                NativeGpuRange slice = range.Slice(4, 12);

                Assert.Equal(
                    (region, 12ul, 12ul, checked(region.GpuAddress + 12)),
                    (slice.Region, slice.Offset, slice.Size, slice.GpuAddress));
            }
            finally { backend.DestroyLinearRegion(region); }
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void DestroyingARegionLeavesTheHeapUsable()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuMemoryRequirements requirements = backend.GetLinearMemoryRequirements(
            64, NativeGpuMemoryKind.CpuVisible);
        NativeGpuHeap heap = backend.CreateGpuHeap(
            requirements.Size, requirements.Alignment,
            NativeGpuMemoryKind.CpuVisible, [requirements.Compatibility]);
        try
        {
            NativeGpuLinearRegion first = backend.CreateLinearRegion(64, heap, 0);
            backend.DestroyLinearRegion(first);

            NativeGpuLinearRegion replacement = backend.CreateLinearRegion(64, heap, 0);
            try
            {
                byte[] expected = [3, 5, 8, 13, 21];
                Marshal.Copy(expected, 0, replacement.CpuAddress, expected.Length);

                Assert.Equal(expected, Read(replacement.CpuAddress, expected.Length));
            }
            finally { backend.DestroyLinearRegion(replacement); }
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    [Theory]
    [InlineData(NativeGpuMemoryKind.CpuVisible)]
    [InlineData(NativeGpuMemoryKind.GpuOnly)]
    [InlineData(NativeGpuMemoryKind.Readback)]
    [Trait("Category", "DirectX12Conformance")]
    public void CpuMappingFollowsTheMemoryKind(NativeGpuMemoryKind kind)
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuMemoryRequirements requirements = backend.GetLinearMemoryRequirements(64, kind);
        NativeGpuHeap heap = backend.CreateGpuHeap(
            requirements.Size, requirements.Alignment, kind, [requirements.Compatibility]);
        try
        {
            NativeGpuLinearRegion region = backend.CreateLinearRegion(64, heap, 0);
            try
            {
                Assert.Equal(
                    (CpuMapped: kind != NativeGpuMemoryKind.GpuOnly, HasGpuAddress: true),
                    (CpuMapped: region.CpuAddress != 0, HasGpuAddress: region.GpuAddress != 0));
            }
            finally { backend.DestroyLinearRegion(region); }
        }
        finally { backend.DestroyGpuHeap(heap); }
    }

    private static byte[] Read(nint address, int length)
    {
        var result = new byte[length];
        Marshal.Copy(address, result, 0, length);
        return result;
    }

    [Theory]
    [InlineData("heap")]
    [InlineData("region")]
    [InlineData("compatibilities")]
    [Trait("Category", "DirectX12Conformance")]
    public void ForeignResourceImplementationsAreRejected(string parameterName)
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        var heap = new ForeignHeap();
        var region = new ForeignRegion(heap);
        Action operation = parameterName switch
        {
            "heap" => () => backend.CreateLinearRegion(64, heap, 0),
            "region" => () => backend.DestroyLinearRegion(region),
            _ => () => backend.CreateGpuHeap(65536, 65536, NativeGpuMemoryKind.GpuOnly,
                [new ForeignCompatibility()]),
        };

        ArgumentException exception = Assert.Throws<ArgumentException>(operation);

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    [Trait("Category", "DirectX12Conformance")]
    public void HeapFromAnotherBackendInstanceIsRejected()
    {
        using DirectX12Backend owner = DirectX12Backend.Create();
        using DirectX12Backend other = DirectX12Backend.Create();
        var requirements = owner.GetLinearMemoryRequirements(64, NativeGpuMemoryKind.GpuOnly);
        NativeGpuHeap heap = owner.CreateGpuHeap(requirements.Size, requirements.Alignment,
            NativeGpuMemoryKind.GpuOnly, [requirements.Compatibility]);
        try
        {
            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => other.CreateLinearRegion(64, heap, 0));

            Assert.Equal("heap", exception.ParamName);
        }
        finally { owner.DestroyGpuHeap(heap); }
    }

    private sealed class ForeignHeap() : NativeGpuHeap(65536, 65536, NativeGpuMemoryKind.GpuOnly);
    private sealed class ForeignRegion(NativeGpuHeap heap) : NativeGpuLinearRegion(heap, 0, 64, 0, 0);
    private sealed class ForeignCompatibility : NativeGpuMemoryCompatibility;
}
