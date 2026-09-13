using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed class VulkanQueueSelectionTests
{
    [Fact]
    public void DedicatedTransferFamilyIsPreferredOverAnotherGraphicsQueue()
    {
        QueueFamilyProperties[] families = [Family(QueueFlags.GraphicsBit | QueueFlags.ComputeBit, 4), Family(QueueFlags.TransferBit, 2)];

        var selection = VulkanBackend.SelectQueues(families);

        Assert.Equal(new VulkanBackend.QueueSelection(0, 1, 0), selection);
    }

    [Fact]
    public void ASecondQueueInTheMainFamilyCanProvideCopies()
    {
        var selection = VulkanBackend.SelectQueues([Family(QueueFlags.GraphicsBit | QueueFlags.ComputeBit, 2)]);

        Assert.Equal(new VulkanBackend.QueueSelection(0, 0, 1), selection);
    }

    [Fact]
    public void ASecondMainQueueIsPreferredOverAnotherComputeFamily()
    {
        var selection = VulkanBackend.SelectQueues([Family(QueueFlags.GraphicsBit | QueueFlags.ComputeBit, 2), Family(QueueFlags.ComputeBit)]);

        Assert.Equal(new VulkanBackend.QueueSelection(0, 0, 1), selection);
    }

    [Fact]
    public void AnotherComputeFamilyCanProvideCopiesWhenMainHasOneQueue()
    {
        var selection = VulkanBackend.SelectQueues([Family(QueueFlags.GraphicsBit | QueueFlags.ComputeBit), Family(QueueFlags.ComputeBit)]);

        Assert.Equal(new VulkanBackend.QueueSelection(0, 1, 0), selection);
    }

    [Fact]
    public void ADeviceWithOnlyOneQueueHasNoCopyQueue()
    {
        var selection = VulkanBackend.SelectQueues([Family(QueueFlags.GraphicsBit | QueueFlags.ComputeBit)]);

        Assert.Equal(new VulkanBackend.QueueSelection(0, null, 0), selection);
    }

    [Fact]
    public void EmptyFamiliesDoNotProvideACopyQueue()
    {
        var selection = VulkanBackend.SelectQueues([Family(QueueFlags.TransferBit, 0), Family(QueueFlags.GraphicsBit | QueueFlags.ComputeBit)]);

        Assert.Equal(new VulkanBackend.QueueSelection(1, null, 0), selection);
    }

    [Fact]
    public void MainQueueRequiresBothGraphicsAndCompute()
    {
        Assert.Null(VulkanBackend.SelectQueues([Family(QueueFlags.GraphicsBit), Family(QueueFlags.ComputeBit)]));
    }

    private static QueueFamilyProperties Family(QueueFlags flags, uint count = 1) => new() { QueueFlags = flags, QueueCount = count };
}
