using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12.Tests;

public sealed partial class DirectX12NativeComputeTests
{
    [Fact]
    public void ChangingDescriptorHeapsBetweenDispatchesUsesEachSelection()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        using var output = new Region(backend, NativeGpuMemoryKind.GpuOnly);
        using var readback = new Region(backend, NativeGpuMemoryKind.Readback);
        using var first = new DescriptorHeap(backend, NativeGpuDescriptorHeapKind.Resource, 4);
        using var second = new DescriptorHeap(backend, NativeGpuDescriptorHeapKind.Resource, 4);
        using var samplers = new DescriptorHeap(backend, NativeGpuDescriptorHeapKind.Sampler, 1);
        NativeGpuComputePipelineHandle pipeline = backend.CreateComputePipeline(Program(GroupShader.Value));
        try
        {
            backend.WriteBufferDescriptor(first.Value, 3, new(output.Value, 512, 16), NativeGpuBufferAccess.ReadWrite);
            backend.WriteBufferDescriptor(second.Value, 3, new(output.Value, 528, 16), NativeGpuBufferAccess.ReadWrite);
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.SetComputePipeline(pipeline);
            commands.SetResourceDescriptorHeap(first.Value);
            commands.SetSamplerDescriptorHeap(samplers.Value);
            commands.Dispatch(Words(3, 0, 11, 1, 1), 1);
            commands.SetResourceDescriptorHeap(second.Value);
            commands.Dispatch(Words(3, 0, 31, 1, 1), 1);
            ReadOutput(commands, output, readback, 512, 32);

            Submit(backend, commands);

            Assert.Equal(new uint[] { 11, 31 }, new[] {
                ReadWords(readback.Value.CpuAddress, 1)[0], ReadWords(readback.Value.CpuAddress + 16, 1)[0] });
        }
        finally { backend.DestroyComputePipeline(pipeline); }
    }

    [Fact]
    public void AShaderWithoutRootInputsCanDispatchWithEmptyRootData()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        byte[] code = Compile("[numthreads(1,1,1)] void computeMain() {}");
        // The fixed directly-indexed root ABI requires both caller-owned heap selections,
        // including for shaders that do not read descriptors or root constants.
        using var resources = new DescriptorHeap(backend, NativeGpuDescriptorHeapKind.Resource, 1);
        using var samplers = new DescriptorHeap(backend, NativeGpuDescriptorHeapKind.Sampler, 1);
        NativeGpuComputePipelineHandle pipeline = backend.CreateComputePipeline(Program(code));
        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.SetComputePipeline(pipeline);
            commands.SetResourceDescriptorHeap(resources.Value);
            commands.SetSamplerDescriptorHeap(samplers.Value);
            commands.Dispatch([], 1);

            Submit(backend, commands);
        }
        finally { backend.DestroyComputePipeline(pipeline); }
    }

    [Fact]
    public void ComputePipelineRejectsARasterProgram()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        var program = new NativeGpuShaderProgram(new NativeGpuShaderCode
        {
            Stage = GpuShaderStage.Vertex, Code = new byte[] { 1 },
        });

        ArgumentException error = Assert.Throws<ArgumentException>(() => backend.CreateComputePipeline(program));

        Assert.Equal("program", error.ParamName);
    }

    [Fact]
    public void NativePipelineCreationRejectsMalformedBytecode()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();

        NativeGpuException error = Assert.Throws<NativeGpuException>(() => backend.CreateComputePipeline(Program([1, 2, 3, 4])));

        Assert.Contains("CreateComputePipelineState", error.Message);
    }

    [Fact]
    public void AForeignPipelineCannotBeSelected()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        using DirectX12Backend other = DirectX12Backend.Create();
        NativeGpuComputePipelineHandle pipeline = other.CreateComputePipeline(Program(GroupShader.Value));
        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();

            ArgumentException error = Assert.Throws<ArgumentException>(() => commands.SetComputePipeline(pipeline));

            Assert.Equal("pipeline", error.ParamName);
        }
        finally { other.DestroyComputePipeline(pipeline); }
    }

    [Fact]
    public void ADestroyedPipelineCannotBeSelectedOrDestroyedAgain()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        NativeGpuComputePipelineHandle pipeline = backend.CreateComputePipeline(Program(GroupShader.Value));
        backend.DestroyComputePipeline(pipeline);
        using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();

        Assert.Throws<ObjectDisposedException>(() => commands.SetComputePipeline(pipeline));
        Assert.Throws<ObjectDisposedException>(() => backend.DestroyComputePipeline(pipeline));
    }

    [Fact]
    public void ADestroyedPipelineRejectsTheBatchBeforeAnyRecordingExecutes()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        using var upload = new Region(backend, NativeGpuMemoryKind.CpuVisible);
        using var readback = new Region(backend, NativeGpuMemoryKind.Readback);
        System.Runtime.InteropServices.Marshal.WriteInt32(upload.Value.CpuAddress, 10);
        System.Runtime.InteropServices.Marshal.WriteInt32(readback.Value.CpuAddress, 77);
        NativeGpuComputePipelineHandle pipeline = backend.CreateComputePipeline(Program(GroupShader.Value));
        using NativeGpuCommandBuffer first = backend.MainQueue.StartCommandRecording();
        using NativeGpuCommandBuffer second = backend.MainQueue.StartCommandRecording();
        using NativeGpuSemaphore completion = backend.MainQueue.CreateSemaphore(0);
        first.CopyMemory(new(upload.Value, 0, 4), new(readback.Value, 0, 4));
        second.SetComputePipeline(pipeline);
        second.Dispatch(Words(3, 0, 0, 1, 1), 1);
        backend.DestroyComputePipeline(pipeline);

        Assert.Throws<ObjectDisposedException>(() => backend.MainQueue.Submit([first, second], completion, 1));
        using NativeGpuCommandBuffer drain = backend.MainQueue.StartCommandRecording();
        backend.MainQueue.Submit([drain], completion, 2);
        backend.MainQueue.Wait(completion, 2);

        Assert.Equal(77u, ReadWords(readback.Value.CpuAddress, 1)[0]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RootBytesCannotBeTruncatedToWholeConstants(bool indirect)
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        using var arguments = new Region(backend, NativeGpuMemoryKind.GpuOnly);
        NativeGpuComputePipelineHandle pipeline = backend.CreateComputePipeline(Program(GroupShader.Value));
        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.SetComputePipeline(pipeline);

            ArgumentException error = Assert.Throws<ArgumentException>(() =>
            {
                if (indirect) { commands.DispatchIndirect(new byte[3], new(arguments.Value, 0, 12)); }
                else { commands.Dispatch(new byte[3], 1); }
            });

            Assert.Equal("rootData", error.ParamName);
        }
        finally { backend.DestroyComputePipeline(pipeline); }
    }

    [Fact]
    public void IndirectDispatchRequiresACompleteLogicalArgumentRange()
    {
        using DirectX12Backend backend = DirectX12Backend.Create();
        using var arguments = new Region(backend, NativeGpuMemoryKind.GpuOnly);
        NativeGpuComputePipelineHandle pipeline = backend.CreateComputePipeline(Program(GroupShader.Value));
        try
        {
            using NativeGpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            commands.SetComputePipeline(pipeline);

            ArgumentException error = Assert.Throws<ArgumentException>(() => commands.DispatchIndirect([], new(arguments.Value, 0, 8)));

            Assert.Equal("arguments", error.ParamName);
        }
        finally { backend.DestroyComputePipeline(pipeline); }
    }
}
