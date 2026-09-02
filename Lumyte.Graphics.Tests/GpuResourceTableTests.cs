using Lumyte.Graphics;

namespace Lumyte.Graphics.Tests;

public sealed class GpuResourceTableTests
{
    [Fact]
    public void LogicalResourcesOccupyFixedIndependentSlots()
    {
        var table = new GpuResourceTable(2, 1);

        table.SetTexture(1, new(12));
        table.SetSampler(0, new(23));

        Assert.Equal(default, table.GetTexture(0));
        Assert.Equal(new TextureId(12), table.GetTexture(1));
        Assert.Equal(new SamplerId(23), table.GetSampler(0));
        Assert.Equal((ulong)2, table.Revision);
    }

    [Fact]
    public void WritingSameResourceDoesNotInvalidateTable()
    {
        var table = new GpuResourceTable(1, 1);
        table.SetTexture(0, new(12));
        ulong revision = table.Revision;

        table.SetTexture(0, new(12));

        Assert.Equal(revision, table.Revision);
    }

    [Fact]
    public void SlotCountCannotChangeAfterCreation()
    {
        var table = new GpuResourceTable(1, 1);

        Assert.Throws<IndexOutOfRangeException>(() => table.SetTexture(1, new(12)));
        Assert.Throws<IndexOutOfRangeException>(() => table.SetSampler(1, new(23)));
    }

    [Fact]
    public void ClearingOccupiedSlotInvalidatesOnlyOnce()
    {
        var table = new GpuResourceTable(1, 1);
        table.SetSampler(0, new(23));

        table.ClearSampler(0);
        ulong revision = table.Revision;
        table.ClearSampler(0);

        Assert.Equal(default, table.GetSampler(0));
        Assert.Equal(revision, table.Revision);
    }

    [Fact]
    public void PublicBackendContractDoesNotExposePhysicalDescriptorTables()
    {
        string[] methodNames = typeof(IGpuBackend).GetMethods().Select(method => method.Name).ToArray();

        Assert.DoesNotContain(methodNames, name => name.Contains("DescriptorHeap", StringComparison.Ordinal));
        Assert.Contains(nameof(IGpuBackend.CreateTextureView), methodNames);
        Assert.Contains(nameof(IGpuBackend.CreateSampler), methodNames);
    }
}
