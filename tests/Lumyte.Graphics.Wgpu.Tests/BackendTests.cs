using System.Runtime.InteropServices;
using Lumyte.Graphics.Wgpu;
using Xunit;
using Buffer = Lumyte.Graphics.Wgpu.Buffer;

namespace Lumyte.Graphics.Wgpu.Tests;

public sealed class BackendTests
{
    private static string Shader(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Shaders", name));
    private static Buffer Readback(WgpuDevice device, ulong size) => device.CreateBuffer(new BufferDesc {
        SizeInBytes = size, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback,
    });

    [Fact]
    public async Task SlangComputeUsesOpaqueReferenceAndReturnsDoubledData()
    {
        using var device = WgpuDevice.Create();
        using var data = device.CreateBuffer(new BufferDesc {
            SizeInBytes = 32, Usage = BufferUsage.CopyDestination | BufferUsage.CopySource | BufferUsage.ShaderWrite,
        });
        using var output = Readback(device, 32);
        using var shader = device.CreateShader(Shader("double.wgsl"));
        using var pipeline = device.CreateComputePipeline(new ComputePipelineDesc { Shader = shader });
        device.WriteBuffer<uint>(data.Slice(0, 32), new uint[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        var reference = device.CreateReference<uint>(data.Slice(0, 32));
        Assert.Equal("GpuReference<UInt32>", reference.ToString());
        using var arguments = pipeline.CreateArguments(reference);
        using var encoder = device.CreateCommandEncoder();
        encoder.Dispatch(pipeline, arguments, 1);
        encoder.CopyBuffer(data.Slice(0, 32), output.Slice(0, 32));
        using var commands = encoder.Finish();
        var submitted = device.Submit(commands);
        Assert.Throws<InvalidOperationException>(() => device.Submit(commands));
        await submitted.WaitAsync();
        Assert.Equal(new uint[] { 2, 4, 6, 8, 10, 12, 14, 16 }, device.ReadBuffer(output));
        Assert.True(submitted.IsCompleted);
    }

    [Fact]
    public void IndexedTriangleAndClearHaveCorrectReadback()
    {
        using var device = WgpuDevice.Create();
        using var target = device.CreateTexture(new TextureDesc { Width = 64, Height = 64 });
        using var view = target.CreateView();
        using var output = Readback(device, 64 * 256);
        using var indices = device.CreateBuffer(new BufferDesc { SizeInBytes = 12, Usage = BufferUsage.Index | BufferUsage.CopyDestination });
        device.WriteBuffer<uint>(indices.Slice(0, 12), new uint[] { 0, 1, 2 });
        using var shader = device.CreateShader(Shader("triangle.wgsl"));
        using var pipeline = device.CreateGraphicsPipeline(new GraphicsPipelineDesc { Shader = shader });
        using var encoder = device.CreateCommandEncoder();
        using (var pass = encoder.BeginRenderPass(new RenderPassDesc { Target = view, ClearValue = new Color4(0, 0, 1, 1) }))
        {
            pass.SetPipeline(pipeline);
            pass.SetIndexBuffer(indices.Slice(0, 12), IndexFormat.Uint32);
            Assert.Throws<ArgumentOutOfRangeException>(() => pass.DrawIndexed(new IndexedDrawDesc { IndexCount = 4 }));
            pass.DrawIndexed(new IndexedDrawDesc { IndexCount = 3 });
        }
        encoder.CopyTextureToBuffer(target, output, 256);
        using var commands = encoder.Finish();
        device.Submit(commands).Wait();
        var result = device.ReadBuffer(output);
        var bytes = MemoryMarshal.AsBytes(result.AsSpan()).ToArray();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, bytes.AsSpan((32 * 64 + 32) * 4, 4).ToArray());
        Assert.Equal(new byte[] { 0, 0, 255, 255 }, bytes.AsSpan(0, 4).ToArray());
    }

    [Fact]
    public void PassStateValidationDoesNotCorruptRecording()
    {
        using var device = WgpuDevice.Create();
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
        using var device = WgpuDevice.Create();
        using var source = device.CreateBuffer(new BufferDesc { SizeInBytes = 16, Usage = BufferUsage.CopySource });
        using var destination = Readback(device, 16);
        using var encoder = device.CreateCommandEncoder();
        encoder.CopyBuffer(source.Slice(0, 16), destination.Slice(0, 16));
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
        using var device = WgpuDevice.Create();
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
    public void NativeShaderValidationIsReportedAndFailedHandleIsReleased()
    {
        using var device = WgpuDevice.Create();
        var error = Assert.Throws<InvalidOperationException>(() => device.CreateShader("not valid WGSL"));
        Assert.Contains("wgpu validation failed", error.Message);
        // No failed module remains registered: device teardown must still succeed.
        device.Dispose();
    }

    [Fact]
    public void InvalidRangesAndCrossDeviceReferencesAreRejected()
    {
        using var first = WgpuDevice.Create();
        using var second = WgpuDevice.Create();
        using var data = first.CreateBuffer(new BufferDesc { SizeInBytes = 16, Usage = BufferUsage.ShaderWrite });
        Assert.Throws<ArgumentOutOfRangeException>(() => data.Slice(ulong.MaxValue, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => data.Slice(12, 8));
        Assert.Throws<ArgumentException>(() => second.CreateReference<uint>(data.Slice(0, 16)));
        Assert.Throws<NotSupportedException>(() => first.CreateReference<float>(data.Slice(0, 16)));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.CreateBuffer(new BufferDesc { SizeInBytes = 3, Usage = BufferUsage.CopySource }));
        Assert.Throws<ArgumentException>(() => first.CreateBuffer(new BufferDesc { SizeInBytes = 16, Usage = (BufferUsage)128 }));
        using var shader = first.CreateShader(Shader("double.wgsl"));
        using var pipeline = first.CreateComputePipeline(new ComputePipelineDesc { Shader = shader });
        Assert.Throws<ArgumentException>(() => pipeline.CreateArguments(default));
    }
}
