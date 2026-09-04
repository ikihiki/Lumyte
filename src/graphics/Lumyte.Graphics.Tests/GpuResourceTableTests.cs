using Lumyte.Graphics;

namespace Lumyte.Graphics.Tests;

public sealed class GpuResourceTableTests
{
    [Fact]
    public void LogicalResourcesOccupyIndependentDescriptorIndices()
    {
        var table = new GpuResourceTable(2, 1, 3);

        table.SetTexture(1, new(12));
        table.SetSampler(0, new(23));
        table.SetBuffer(2, new(34));

        Assert.Equal(default, table.GetTexture(0));
        Assert.Equal(new TextureId(12), table.GetTexture(1));
        Assert.Equal(new SamplerId(23), table.GetSampler(0));
        Assert.Equal(new BufferId(34), table.GetBuffer(2));
        Assert.Equal((ulong)3, table.Revision);
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
    public void BufferDescriptorChangesAdvanceRevisionOnlyWhenItsValueChanges()
    {
        var table = new GpuResourceTable(0, 0, 1);
        var buffer = new BufferId(34);
        table.SetBuffer(0, buffer);
        ulong populatedRevision = table.Revision;

        table.SetBuffer(0, buffer);
        table.ClearBuffer(0);
        ulong clearedRevision = table.Revision;
        table.ClearBuffer(0);

        Assert.Equal(populatedRevision + 1, clearedRevision);
        Assert.Equal(clearedRevision, table.Revision);
        Assert.Equal(default, table.GetBuffer(0));
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
