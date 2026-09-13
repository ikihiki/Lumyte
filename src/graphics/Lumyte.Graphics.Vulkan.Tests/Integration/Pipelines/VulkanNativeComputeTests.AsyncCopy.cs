using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe partial class VulkanNativeComputeTests
{
    [VulkanCopyFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void CpuCanSubmitThreeCopyAndComputeFramesBeforeTheGpuGateOpens()
    {
        using var resources = new Resources();
        var backend = resources.Backend;
        var copy = backend.CopyQueue!;
        var main = backend.MainQueue;
        var pipeline = resources.Pipeline("NativeReadOnlyCompute.spv", "readOnlyMain");
        var upload = resources.Linear(256, NativeGpuMemoryKind.CpuVisible);
        var input = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
        var output = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
        var readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
        var descriptors = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 8);
        using var gate = backend.CreateSemaphore();
        using var uploaded = backend.CreateSemaphore();
        using var rendered = backend.CreateSemaphore();
        for (uint frame = 0; frame < 3; frame++)
        {
            Words(upload)[checked((int)frame * 16)] = 41 + frame * 10;
            backend.WriteBufferDescriptor(descriptors, frame + 2, new(input, frame * 64, 4), NativeGpuBufferAccess.ReadOnly);
        }
        ulong lastCopy = 0;
        ulong lastMain = 0;
        try
        {
            for (uint frame = 0; frame < 3; frame++)
            {
                using var transfer = copy.StartCommandRecording();
                transfer.CopyMemory(new(upload, frame * 64, 4), new(input, frame * 64, 4));
                copy.Submit([transfer], new(uploaded, frame + 1), [new(gate, 1)]);
                lastCopy = frame + 1;

                using var commands = main.StartCommandRecording();
                commands.SetComputePipeline(pipeline);
                commands.SetResourceDescriptorHeap(descriptors);
                commands.Dispatch(Root(new NativeGpuRange(output, frame * 64, 4).GpuAddress, 100, buffer: frame + 2), 1);
                Readback(commands, new(output, frame * 64, 4), new(readback, frame * 64, 4));
                main.Submit([commands], new(rendered, frame + 1), [new(uploaded, frame + 1)]);
                lastMain = frame + 1;
            }

            // Every Submit and recording disposal returned while both queues remain behind the gate.
            Assert.False(uploaded.IsComplete(1));
            Assert.False(rendered.IsComplete(1));
        }
        finally
        {
            gate.SignalCpu(1);
            if (lastMain != 0) { rendered.WaitCpu(lastMain); }
            if (lastCopy != 0) { uploaded.WaitCpu(lastCopy); }
        }

        Assert.Equal(new uint[] { 141, 151, 161 }, new[] { Words(readback)[0], Words(readback)[16], Words(readback)[32] });
    }
}
