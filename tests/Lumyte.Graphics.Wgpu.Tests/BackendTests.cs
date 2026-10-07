using System.Runtime.InteropServices;
using Lumyte.Graphics;
using Xunit;

namespace Lumyte.Graphics.Tests;

public sealed class BackendTests
{
    private static IGraphicsBuffer<T> Readback<T>(GraphicsDevice device, ulong count) where T : unmanaged => device.CreateBuffer(new BufferDesc<T> {
        Count = count, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback,
    });

    private static uint[] ReadWords(IGraphicsBuffer<uint> buffer)
    {
        var words = new uint[checked((int)buffer.Count)];
        buffer.CopyTo(words.AsSpan());
        return words;
    }

    private readonly record struct Pair(int X, float Y);

    [Fact]
    public void TypedBuffersComputeSizesAndCopyElementRangesAcrossTypes()
    {
        using var device = Graphics.CreateDevice();
        using var upload = device.CreateBuffer(new BufferDesc<Pair> {
            Count = 3, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload,
        });
        Assert.Equal(3UL, upload.Count);
        Assert.Equal(24UL, upload.SizeInBytes);
        upload.CopyFrom(new Pair[] { new(1, 1.5f), new(2, 2.5f), new(3, 3.5f) });
        var part = upload.Slice(1, 2);
        Assert.Same(upload, part.Buffer);
        Assert.Equal(8UL, part.OffsetInBytes);
        Assert.Equal(16UL, part.SizeInBytes);
        part.CopyFrom(new Pair[] { new(4, 4.5f) });
        Assert.Throws<ArgumentException>(() => part.CopyFrom(new Pair[3]));
        Assert.Throws<ArgumentOutOfRangeException>(() => upload.Slice(2, 2));
        using var result = Readback<byte>(device, 16);
        using var encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(part, result.Slice(0, 16));
        using var commands = encoder.Finish();
        device.Submit(commands).Wait();
        var bytes = new byte[16];
        result.CopyTo(bytes);
        Assert.Equal(new Pair[] { new(4, 4.5f), new(3, 3.5f) }, MemoryMarshal.Cast<byte, Pair>(bytes).ToArray());
        using var foreign = Graphics.CreateDevice();
        using var foreignEncoder = foreign.CreateCommandEncoder();
        Assert.Throws<ArgumentException>(() => foreignEncoder.RecordCopyBuffer(part, result.Slice(0, 16)));
        // The backend object implements both contracts; no common resource wrapper is allocated.
        Assert.False(upload is GpuResource);
    }

    [Fact]
    public void TypedSizesUseManagedLayoutAndRejectOverflowBeforeAllocation()
    {
        Assert.Equal(4UL, new BufferDesc<byte> { Count = 4, Usage = BufferUsage.CopySource }.SizeInBytes);
        Assert.Equal(4UL, new BufferDesc<ushort> { Count = 2, Usage = BufferUsage.CopySource }.SizeInBytes);
        Assert.Equal(16UL, new BufferDesc<double> { Count = 2, Usage = BufferUsage.CopySource }.SizeInBytes);
        Assert.Equal(4UL, new BufferDesc<bool> { Count = 4, Usage = BufferUsage.CopySource }.SizeInBytes);
        using var device = Graphics.CreateDevice();
        Assert.Throws<OverflowException>(() => device.CreateBuffer(new BufferDesc<double> {
            Count = ulong.MaxValue, Usage = BufferUsage.CopySource,
        }));
        Assert.Throws<ArgumentOutOfRangeException>(() => device.CreateBuffer(new BufferDesc<uint> {
            Count = 0, Usage = BufferUsage.CopySource,
        }));
        Assert.Throws<ArgumentNullException>(() => device.CreateBuffer<uint>(null!));
        Assert.Throws<ArgumentException>(() => device.CreateReference(default(BufferSlice<uint>)));
        device.Dispose();
    }

    [Fact]
    public void CpuReadCopyUsesCallerMemoryAndValidatesRanges()
    {
        using var device = Graphics.CreateDevice();
        using var upload = device.CreateBuffer(new BufferDesc<byte> {
            Count = 16, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload,
        });
        using var readback = Readback<byte>(device, 16);
        upload.CopyFrom(Enumerable.Range(0, 16).Select(x => (byte)x).ToArray());
        upload.Slice(3, 4).CopyFrom(new byte[] { 30, 40 });
        using var encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(upload.Slice(0, 16), readback.Slice(0, 16));
        using var commands = encoder.Finish();
        device.Submit(commands).Wait();
        var destination = Enumerable.Repeat((byte)255, 8).ToArray();
        readback.Slice(3, 4).CopyTo(destination.AsSpan());
        Assert.Equal(new byte[] { 30, 40, 5, 6, 255, 255, 255, 255 }, destination);
        Assert.Throws<ArgumentException>(() => readback.Slice(0, 16).CopyTo(new byte[4].AsSpan()));
        Assert.Throws<ArgumentException>(() => upload.Slice(0, 4).CopyTo(new byte[4].AsSpan()));
        Assert.Throws<ArgumentException>(() => default(BufferSlice<byte>).CopyTo(new byte[4].AsSpan()));
        Assert.Throws<ArgumentException>(() => default(BufferSlice<byte>).CopyFrom(new byte[4]));
        Assert.Throws<ArgumentException>(() => upload.CopyFrom(new byte[17]));
        var stale = upload.Slice(0, 4);
        upload.Dispose();
        Assert.Throws<ObjectDisposedException>(() => stale.CopyFrom(new byte[4]));
    }

    [Fact]
    public void CpuCopyOnlyWritesUploadMemoryAndTransferRequiresCommands()
    {
        using var device = Graphics.CreateDevice();
        using var upload = device.CreateBuffer(new BufferDesc<uint> {
            Count = 4, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload,
        });
        using var gpu = device.CreateBuffer(new BufferDesc<uint> {
            Count = 4, Usage = BufferUsage.CopyDestination,
        });
        Assert.Throws<ArgumentException>(() => gpu.Slice(0, 4).CopyFrom(new uint[] { 1, 2, 3, 4 }));
        Assert.Throws<ArgumentException>(() => device.CreateBuffer(new BufferDesc<uint> {
            Count = 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Upload,
        }));
        upload.Slice(0, 4).CopyFrom(new uint[] { 1, 2, 3, 4 });
        upload.Slice(0, 4).CopyFrom(new uint[] { 5, 6, 7, 8 });
        using var output = Readback<uint>(device, 4);
        using var encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(upload.Slice(0, 4), output.Slice(0, 4));
        Assert.Throws<InvalidOperationException>(() => upload.Slice(0, 4).CopyFrom(new uint[] { 9 }));
        Assert.Throws<InvalidOperationException>(() => ReadWords(output));
        using var commands = encoder.Finish();
        device.Submit(commands).Wait();
        Assert.Equal(new uint[] { 5, 6, 7, 8 }, ReadWords(output));
    }

    [Fact]
    public void ConsumersAndCoreHaveNoBackendAssemblyReferences()
    {
        foreach (var assembly in new[] { typeof(BackendTests).Assembly, typeof(GraphicsDevice).Assembly })
        {
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), name =>
                name.Name == "Lumyte.Graphics.Wgpu" || name.Name!.StartsWith("Ahjo."));
        }
    }

    [Fact]
    public void BackendSelectionAndCrossDeviceCommandsAreValidatedThroughCommonApi()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Graphics.CreateDevice((GraphicsBackend)999));
        using var first = Graphics.CreateDevice();
        using var second = Graphics.CreateDevice();
        using var target = first.CreateTexture(new TextureDesc { Width = 4, Height = 4 });
        using var view = target.CreateView();
        using var encoder = second.CreateCommandEncoder();
        Assert.Throws<ArgumentException>(() => encoder.BeginRenderPass(new RenderPassDesc { Target = view }));
        using var shader = first.CreateShader(typeof(BackendTests).Assembly, "Lumyte.Shaders.double.wgsl");
        Assert.Throws<ArgumentException>(() => second.CreateComputePipeline(new ComputePipelineDesc { Shader = shader }));
        using var commands = encoder.Finish();
        Assert.Throws<ArgumentException>(() => first.Submit(commands));
        second.Submit(commands).Wait();
    }

    [Fact]
    public void OfflineShadersAreEmbeddedWithoutDeploymentSidecars()
    {
        var assembly = typeof(BackendTests).Assembly;
        foreach (var name in new[] { "double", "triangle" })
        {
            using var stream = assembly.GetManifestResourceStream($"Lumyte.Shaders.{name}.wgsl");
            Assert.NotNull(stream);
            Assert.True(stream.Length > 0);
        }
        Assert.Empty(Directory.GetFiles(AppContext.BaseDirectory, "*.wgsl", SearchOption.AllDirectories));
        using var device = Graphics.CreateDevice();
        Assert.Throws<ArgumentException>(() => device.CreateShader(assembly, "missing.wgsl"));
    }

    [Fact]
    public async Task SlangComputeUsesOpaqueReferenceAndReturnsDoubledData()
    {
        using var device = Graphics.CreateDevice();
        using var data = device.CreateBuffer(new BufferDesc<uint> {
            Count = 8, Usage = BufferUsage.CopyDestination | BufferUsage.CopySource | BufferUsage.ShaderWrite,
        });
        using var output = Readback<uint>(device, 8);
        using var shader = device.CreateShader(typeof(BackendTests).Assembly, "Lumyte.Shaders.double.wgsl");
        using var pipeline = device.CreateComputePipeline(new ComputePipelineDesc { Shader = shader });
        using var upload = device.CreateBuffer(new BufferDesc<uint> {
            Count = 8, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload,
        });
        upload.CopyFrom(new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var reference = device.CreateReference<uint>(data.Slice(0, 8));
        Assert.Equal("GpuReference<UInt32>", reference.ToString());
        using var arguments = pipeline.CreateArguments(reference);
        using var encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(upload.Slice(0, 8), data.Slice(0, 8));
        encoder.Dispatch(pipeline, arguments, 1);
        encoder.RecordCopyBuffer(data.Slice(0, 8), output.Slice(0, 8));
        using var commands = encoder.Finish();
        var submitted = device.Submit(commands);
        Assert.Throws<InvalidOperationException>(() => device.Submit(commands));
        await submitted.WaitAsync();
        Assert.Equal(new uint[] { 2, 4, 6, 8, 10, 12, 14, 16 }, ReadWords(output));
        Assert.True(submitted.IsCompleted);
    }

    [Fact]
    public void IndexedTriangleAndClearHaveCorrectReadback()
    {
        using var device = Graphics.CreateDevice();
        using var target = device.CreateTexture(new TextureDesc { Width = 64, Height = 64 });
        using var view = target.CreateView();
        using var output = Readback<uint>(device, 64 * 256 / sizeof(uint));
        using var indices = device.CreateBuffer(new BufferDesc<uint> { Count = 3, Usage = BufferUsage.Index | BufferUsage.CopyDestination });
        using var upload = device.CreateBuffer(new BufferDesc<uint> {
            Count = 3, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload,
        });
        upload.Slice(0, 3).CopyFrom(new uint[] { 0, 1, 2 });
        using var shader = device.CreateShader(typeof(BackendTests).Assembly, "Lumyte.Shaders.triangle.wgsl");
        using var pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader });
        using var encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(upload.Slice(0, 3), indices.Slice(0, 3));
        using (var pass = encoder.BeginRenderPass(new RenderPassDesc { Target = view, ClearValue = new Color4(0, 0, 1, 1) }))
        {
            pass.SetPipeline(pipeline);
            Assert.Throws<ArgumentException>(() => pass.SetIndexBuffer(indices.Slice(0, 3), IndexFormat.Uint16));
            pass.SetIndexBuffer(indices.Slice(0, 3), IndexFormat.Uint32);
            Assert.Throws<ArgumentOutOfRangeException>(() => pass.DrawIndexed(new IndexedDrawDesc { IndexCount = 4 }));
            pass.DrawIndexed(new IndexedDrawDesc { IndexCount = 3 });
        }
        encoder.RecordCopyTextureToBuffer(target, output, 256);
        using var commands = encoder.Finish();
        device.Submit(commands).Wait();
        var result = ReadWords(output);
        var bytes = MemoryMarshal.AsBytes(result.AsSpan()).ToArray();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, bytes.AsSpan((32 * 64 + 32) * 4, 4).ToArray());
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, bytes.AsSpan(0, 4).ToArray());
    }

    [Fact]
    public void PassStateValidationDoesNotCorruptRecording()
    {
        using var device = Graphics.CreateDevice();
        using var target = device.CreateTexture(new TextureDesc { Width = 64, Height = 64 });
        using var view = target.CreateView();
        using var encoder = device.CreateCommandEncoder();
        using var pass = encoder.BeginRenderPass(new RenderPassDesc { Target = view });
        Assert.Throws<InvalidOperationException>(() => encoder.Finish());
        Assert.Throws<InvalidOperationException>(() => encoder.BeginRenderPass(new RenderPassDesc { Target = view }));
        Assert.Throws<InvalidOperationException>(() => pass.Draw(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => pass.SetViewport(new Viewport(float.NaN, 0, 64, 64)));
        Assert.Throws<ArgumentOutOfRangeException>(() => pass.SetScissor(new Scissor(uint.MaxValue, 0, 2, 64)));
        pass.SetViewport(new Viewport(0, 0, 64, 64));
        pass.SetScissor(new Scissor(0, 0, 64, 64));
        pass.End();
        Assert.Throws<InvalidOperationException>(pass.End);
        Assert.Throws<InvalidOperationException>(() => pass.SetScissor(new Scissor(0, 0, 1, 1)));
        pass.Dispose();
        using var commands = encoder.Finish();
        device.Submit(commands).Wait();
    }

    [Fact]
    public void ResourcesAreHeldThroughRecordingAndSubmission()
    {
        using var device = Graphics.CreateDevice();
        using var source = device.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopySource });
        using var destination = Readback<uint>(device, 4);
        using var encoder = device.CreateCommandEncoder();
        encoder.RecordCopyBuffer(source.Slice(0, 4), destination.Slice(0, 4));
        Assert.Throws<InvalidOperationException>(source.Dispose);
        using var commands = encoder.Finish();
        var submission = device.Submit(commands);
        Assert.Throws<InvalidOperationException>(source.Dispose);
        Assert.Throws<InvalidOperationException>(device.Dispose);
        submission.Wait();
        source.Dispose(); source.Dispose();
        Assert.Throws<ObjectDisposedException>(() => source.Slice(0, 4));
    }

    [Fact]
    public void DiscardingEncoderReleasesResourcesAndInvalidatesOpenPass()
    {
        using var device = Graphics.CreateDevice();
        using var target = device.CreateTexture(new TextureDesc { Width = 64, Height = 64 });
        using var view = target.CreateView();
        using var encoder = device.CreateCommandEncoder();
        using var pass = encoder.BeginRenderPass(new RenderPassDesc { Target = view });
        encoder.Dispose();
        Assert.Throws<InvalidOperationException>(() => pass.Draw(3));
        pass.Dispose();
        view.Dispose();
        target.Dispose();
    }

    [Fact]
    public void InvalidEmbeddedShaderRequestsDoNotRetainResources()
    {
        using var device = Graphics.CreateDevice();
        Assert.Throws<ArgumentNullException>(() => device.CreateShader(null!, "missing"));
        Assert.Throws<ArgumentException>(() => device.CreateShader(typeof(BackendTests).Assembly, ""));
        Assert.Throws<ArgumentException>(() => device.CreateShader(typeof(BackendTests).Assembly, "missing"));
        device.Dispose();
    }

    [Fact]
    public void InvalidRangesAndCrossDeviceReferencesAreRejected()
    {
        using var first = Graphics.CreateDevice();
        using var second = Graphics.CreateDevice();
        using var data = first.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.ShaderWrite });
        Assert.Throws<ArgumentOutOfRangeException>(() => data.Slice(ulong.MaxValue, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.Slice(3, 2));
        Assert.Throws<ArgumentException>(() => second.CreateReference<uint>(data.Slice(0, 4)));
        using var floatData = first.CreateBuffer(new BufferDesc<float> { Count = 4, Usage = BufferUsage.ShaderWrite });
        Assert.Throws<NotSupportedException>(() => first.CreateReference<float>(floatData.Slice(0, 4)));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.CreateBuffer(new BufferDesc<byte> { Count = 3, Usage = BufferUsage.CopySource }));
        Assert.Throws<ArgumentException>(() => first.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = (BufferUsage)128 }));
        using var shader = first.CreateShader(typeof(BackendTests).Assembly, "Lumyte.Shaders.double.wgsl");
        using var pipeline = first.CreateComputePipeline(new ComputePipelineDesc { Shader = shader });
        Assert.Throws<ArgumentException>(() => pipeline.CreateArguments(default));
    }
}
