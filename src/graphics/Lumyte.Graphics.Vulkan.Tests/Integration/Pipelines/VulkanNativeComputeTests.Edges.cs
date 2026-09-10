using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.Vulkan.Tests;

public sealed unsafe partial class VulkanNativeComputeTests
{
    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void StorageTextureDescriptorsWriteAfterAnExplicitDiscard()
    {
        using var resources = new Resources();
        var pipeline = resources.Pipeline("NativeStorageCompute.spv", "storageMain");
        var texture = resources.Texture(storage: true);
        var readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
        var heap = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 8);
        resources.Backend.WriteTextureDescriptor(heap, 5, View(texture), NativeGpuTextureDescriptorType.Storage);
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.SetComputePipeline(pipeline);
        commands.SetResourceDescriptorHeap(heap);
        commands.DiscardTexture(View(texture), GpuTextureLayout.General);
        commands.Dispatch(Root(0, 173, texture: 5), 1);
        commands.Barrier(GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.Copy, GpuAccess.CopyRead);
        commands.CopyTextureToMemory(texture, new(readback, 0, 4),
            new(0, NativeGpuTextureAspect.Color, 0, 1, default, new(1, 1, 1), 4, 4));
        commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);

        Assert.Equal(new byte[] { 173, 0, 255, 255 }, Bytes(readback)[..4].ToArray());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DispatchAcceptsAnEmptyRootForAHeapOnlyShader()
    {
        using var resources = new Resources();
        var pipeline = resources.Pipeline("NativeEmptyRootCompute.spv", "emptyRootMain");
        var output = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
        var readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
        var heap = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 4);
        resources.Backend.WriteBufferDescriptor(heap, 3, new(output, 0, 256), NativeGpuBufferAccess.ReadWrite);
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.SetComputePipeline(pipeline);
        commands.SetResourceDescriptorHeap(heap);
        commands.Dispatch([], 1);
        Readback(commands, new(output, 0, 4), new(readback, 0, 4));

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);

        Assert.Equal(97u, Words(readback)[0]);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ReadOnlyBufferDescriptorsReadTheSelectedRange()
    {
        using var resources = new Resources();
        var pipeline = resources.Pipeline("NativeReadOnlyCompute.spv", "readOnlyMain");
        var input = resources.Linear(256, NativeGpuMemoryKind.CpuVisible);
        var output = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
        var readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
        Words(input)[16] = 713;
        var heap = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 8);
        resources.Backend.WriteBufferDescriptor(heap, 5, new(input, 64, 64), NativeGpuBufferAccess.ReadOnly);
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.SetComputePipeline(pipeline);
        commands.SetResourceDescriptorHeap(heap);
        commands.Dispatch(Root(new NativeGpuRange(output, 0, 256).GpuAddress, 18, buffer: 5), 1);
        Readback(commands, new(output, 0, 4), new(readback, 0, 4));

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);

        Assert.Equal(731u, Words(readback)[0]);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DispatchPreservesAllThreeGroupCounts()
    {
        using var resources = new Resources();
        var pipeline = resources.Pipeline("NativeGroupsCompute.spv", "groupsMain");
        var output = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
        var readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.SetComputePipeline(pipeline);
        commands.Dispatch(Root(new NativeGpuRange(output, 0, 256).GpuAddress, 0), 2, 3, 2);
        Readback(commands, new(output, 0, 48), new(readback, 0, 48));

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);

        Assert.Equal(new uint[] { 0, 1, 10, 11, 20, 21, 100, 101, 110, 111, 120, 121 }, Words(readback)[..12].ToArray());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void SwitchingResourceHeapsChangesTheDestination()
    {
        using var resources = new Resources();
        var pipeline = resources.Pipeline("NativeEmptyRootCompute.spv", "emptyRootMain");
        var output = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
        var readback = resources.Linear(256, NativeGpuMemoryKind.Readback);
        var first = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 4);
        var second = resources.Descriptors(NativeGpuDescriptorHeapKind.Resource, 4);
        resources.Backend.WriteBufferDescriptor(first, 3, new(output, 0, 64), NativeGpuBufferAccess.ReadWrite);
        resources.Backend.WriteBufferDescriptor(second, 3, new(output, 64, 64), NativeGpuBufferAccess.ReadWrite);
        var queue = resources.Backend.MainQueue;
        using var completion = queue.CreateSemaphore(0);
        using var commands = queue.StartCommandRecording();
        commands.SetComputePipeline(pipeline);
        commands.SetResourceDescriptorHeap(first);
        commands.Dispatch([], 1);
        commands.SetResourceDescriptorHeap(second);
        commands.Dispatch([], 1);
        commands.Barrier(GpuStage.ComputeShader, GpuAccess.ShaderWrite, GpuStage.Copy, GpuAccess.CopyRead);
        commands.CopyMemory(new(output, 0, 4), new(readback, 0, 4));
        commands.CopyMemory(new(output, 64, 4), new(readback, 4, 4));
        commands.Barrier(GpuStage.Copy, GpuAccess.CopyWrite, GpuStage.Host, GpuAccess.HostRead);

        queue.Submit([commands], completion, 1);
        queue.Wait(completion, 1);

        Assert.Equal(new uint[] { 97, 97 }, Words(readback)[..2].ToArray());
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ForeignComputePipelinesAreRejected()
    {
        using var resources = new Resources();
        using var commands = resources.Backend.MainQueue.StartCommandRecording();

        var error = Assert.Throws<ArgumentException>(() => commands.SetComputePipeline(new ForeignPipeline()));

        Assert.Equal("pipeline", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void DestroyedComputePipelinesCannotBeSelected()
    {
        using var backend = VulkanBackend.Create();
        var pipeline = backend.CreateComputePipeline(new(new NativeGpuShaderCode
        {
            Stage = GpuShaderStage.Compute, Code = ShaderBytes("NativeRawCompute.spv"), EntryPoint = "rawMain",
        }));
        backend.DestroyComputePipeline(pipeline);
        using var commands = backend.MainQueue.StartCommandRecording();

        Assert.Throws<ObjectDisposedException>(() => commands.SetComputePipeline(pipeline));
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void IndirectArgumentsRespectTheLogicalRange()
    {
        using var resources = new Resources();
        var arguments = resources.Linear(256, NativeGpuMemoryKind.GpuOnly);
        using var commands = resources.Backend.MainQueue.StartCommandRecording();

        var error = Assert.Throws<ArgumentException>(() => commands.DispatchIndirect([], new(arguments, 0, 8)));

        Assert.Equal("arguments", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ShaderCodeCannotLoseATrailingByteDuringWordConversion()
    {
        using var backend = VulkanBackend.Create();
        var program = new NativeGpuShaderProgram(new NativeGpuShaderCode { Stage = GpuShaderStage.Compute, Code = new byte[5] });

        var error = Assert.Throws<ArgumentException>(() => backend.CreateComputePipeline(program));

        Assert.Equal("program", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void ShaderEntryPointsCannotBeTruncatedAtAnEmbeddedNull()
    {
        using var backend = VulkanBackend.Create();
        var program = new NativeGpuShaderProgram(new NativeGpuShaderCode
        {
            Stage = GpuShaderStage.Compute, Code = ShaderBytes("NativeRawCompute.spv"), EntryPoint = "rawMain\0ignored",
        });

        var error = Assert.Throws<ArgumentException>(() => backend.CreateComputePipeline(program));

        Assert.Equal("program", error.ParamName);
    }

    [VulkanNativeFact]
    [Trait("Category", "VulkanNativeConformance")]
    public void RasterProgramsCannotBecomeComputePipelines()
    {
        using var backend = VulkanBackend.Create();
        var program = new NativeGpuShaderProgram(new NativeGpuShaderCode { Stage = GpuShaderStage.Vertex, Code = new byte[4] });

        var error = Assert.Throws<ArgumentException>(() => backend.CreateComputePipeline(program));

        Assert.Equal("program", error.ParamName);
    }

    private sealed class ForeignPipeline : NativeGpuComputePipelineHandle;
}
