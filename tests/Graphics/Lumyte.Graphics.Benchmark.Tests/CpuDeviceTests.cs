using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Xunit;

namespace Lumyte.Graphics.Benchmark.Tests;

/// <summary>Functional contracts for managed recording, not hardware rendering or timing.</summary>
public sealed class CpuDeviceTests
{
    /// <summary>Verifies the managed CPU device contract.</summary>
    /// <returns>The completed device contract check.</returns>
    [Fact]
    public async Task UploadConsumesExactBytesAndResetPreservesOwnershipAsync()
    {
        using var device = new CpuGraphicsDevice();
        using IGraphicsBuffer<int> upload = device.CreateBuffer<int>(new() { Count = 4, Memory = MemoryPreference.Upload, Usage = BufferUsage.CopySource });
        Assert.Equal(16UL, upload.SizeInBytes);
        Assert.Throws<InvalidOperationException>(() => upload.CopyFrom(new int[] { 1 }));
        await upload.MapAsync();
        upload.SliceElements(1, 2).CopyFrom(new int[] { 17, 23 });
        Assert.Equal(8, device.Counters.UploadedBytes);
        Assert.NotEqual(0UL, device.Counters.Checksum);
        Assert.Throws<ArgumentOutOfRangeException>(() => upload.SliceElements(3, 2));
        Assert.Throws<ArgumentException>(() => upload.CopyFrom(new int[5]));
        upload.Unmap();
        device.ResetCounters();
        Assert.Equal(0, device.Counters.UploadedBytes);
        Assert.Equal(1, device.Counters.LiveResources);
        Assert.Throws<InvalidOperationException>(() => device.Dispose());
        upload.Dispose();
        upload.Dispose();
        Assert.Equal(0, device.Counters.LiveResources);
        Assert.Throws<ObjectDisposedException>(() => upload.MapAsync());
    }

    /// <summary>Verifies the managed CPU device contract.</summary>
    [Fact]
    public void CopiesAreValidatedAndSubmissionIsOneShotSimulatedCompletion()
    {
        using var device = new CpuGraphicsDevice();
        using var other = new CpuGraphicsDevice();
        using IGraphicsBuffer<int> source = device.CreateBuffer<int>(new() { Count = 2, Usage = BufferUsage.CopySource });
        using IGraphicsBuffer<byte> target = device.CreateBuffer<byte>(new() { Count = 8, Usage = BufferUsage.CopyDestination });
        using IGraphicsBuffer<byte> foreign = other.CreateBuffer<byte>(new() { Count = 8, Usage = BufferUsage.CopyDestination });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        Assert.Throws<ArgumentException>(() => commands.CopyBuffer(source.SliceElements(0, 2), foreign.SliceElements(0, 8)));
        commands.CopyBuffer(source.SliceElements(0, 2), target.SliceElements(0, 8));
        commands.Barrier(new MemoryBarrierDesc { Before = default, After = default });
        Assert.Equal(1, device.Counters.Copies);
        Assert.Equal(1, device.Counters.BarrierCalls);
        Assert.Throws<InvalidOperationException>(() => device.Queue.Submit(new[] { commands }));
        commands.Finish();
        Assert.Equal(CommandBufferState.Executable, commands.State);
        using IGraphicsSubmission submission = device.Queue.Submit(new[] { commands });
        Assert.Equal(CommandBufferState.Completed, commands.State);
        Assert.Equal(SubmissionStatus.Completed, submission.Status);
        Assert.Equal(1, device.Counters.Submissions);
        Assert.Throws<InvalidOperationException>(() => device.Queue.Submit(new[] { commands }));
        Assert.Throws<InvalidOperationException>(() => commands.Finish());
    }

    /// <summary>Verifies the managed CPU device contract.</summary>
    [Fact]
    public void ViewAndPassLifetimesRejectInvalidUse()
    {
        using var device = new CpuGraphicsDevice();
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 32, Height = 16, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView view = texture.CreateView();
        Assert.Throws<InvalidOperationException>(() => texture.Dispose());
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        IRenderEncoder pass = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
        Assert.Throws<InvalidOperationException>(() => commands.Finish());
        Assert.Throws<ArgumentOutOfRangeException>(() => pass.SetViewport(new(0, 0, float.NaN, 16, 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => pass.SetScissor(new(0, 0, 33, 16)));
        pass.SetScissor(new(0, 0, 32, 16));
        pass.End();
        Assert.Throws<InvalidOperationException>(() => pass.End());
        commands.Finish();
        Assert.Equal(1, device.Counters.RenderPasses);
    }

    /// <summary>Verifies the managed CPU device contract.</summary>
    /// <returns>The completed device contract check.</returns>
    [Fact]
    public async Task BulkShaderCodecUsesStaticWireStrideAndCountsActualWritesAsync()
    {
        using var device = new CpuGraphicsDevice();

        // The current C#-authoritative ABI does not require reflection to allocate storage.
        using IGraphicsShaderDataBuffer<NumericArguments> data = device.CreateShaderDataBuffer<NumericArguments>(null!, 3, MemoryPreference.Upload);
        Assert.Equal(12UL, data.SizeInBytes);
        await data.MapAsync();
        data.CopyFrom(new NumericArguments[] { new(41), new(99) }, 1);
        Assert.Equal(8, device.Counters.UploadedBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => data.CopyFrom(new NumericArguments[] { new(7) }, 3));
        data.Unmap();
        Assert.Throws<InvalidOperationException>(() => data.CopyFrom(new NumericArguments[] { new(7) }));
    }

    /// <summary>Verifies the managed CPU device contract.</summary>
    [Fact]
    public void TableRejectsForeignResourcesAndReleasedReferences()
    {
        using var device = new CpuGraphicsDevice();
        using var other = new CpuGraphicsDevice();
        using IGraphicsBuffer<int> buffer = device.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.ShaderRead });
        using IGraphicsBuffer<int> foreign = other.CreateBuffer<int>(new() { Count = 4, Usage = BufferUsage.ShaderRead });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 1 });
        Assert.Throws<ArgumentException>(() => table.WriteBuffer(0, foreign.SliceElements(0, 4)));
        IGpuRef<int> reference = table.WriteBuffer(0, buffer.SliceElements(0, 4));
        Assert.Equal(1UL, reference.GetElement(2).Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => reference.GetElement(4));
        table.WriteBuffer(0, buffer.SliceElements(0, 4));
        Assert.Throws<InvalidOperationException>(() => reference.GetElement(0));
        IGpuRef<int> current = table.WriteBuffer(0, buffer.SliceElements(0, 4));
        table.ReleaseBuffer(0);
        Assert.Throws<InvalidOperationException>(() => current.GetElement(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => table.WriteBuffer(1, buffer.SliceElements(0, 4)));
    }

    /// <summary>Mesh APIs remain explicit rejections on the non-rasterizing CPU recorder.</summary>
    [Fact]
    public void MeshExecutionIsNotAdvertisedOrSilentlyRecorded()
    {
        using var device = new CpuGraphicsDevice();
        Assert.Equal(GraphicsFeatures.None, device.Caps.Features & (GraphicsFeatures.MeshShader | GraphicsFeatures.TaskShader));
        Assert.Throws<NotSupportedException>(() => device.CreateMeshPipeline(new() { MeshShader = null! }));
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        IRenderEncoder pass = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
        Assert.Throws<NotSupportedException>(() => pass.SetPipeline((IMeshPipeline)null!));
        Assert.Throws<NotSupportedException>(() => pass.DrawMeshTasks(1));
        Assert.Equal(0, device.Counters.Draws);
        pass.End();
        commands.Finish();
    }

    private readonly record struct NumericArguments(int Value) : IShaderArguments
    {
        public static string ShaderTypeName => "NumericArguments";

        public static int TransferSize => 4;

        public static int ReferenceCount => 0;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            int value = Value;
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
        }
    }
}
