using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Checks mesh admission without loading a Windows driver.</summary>
public sealed class MeshPipelineTests
{
    /// <summary>An unsupported device must remain constructible and reject mesh explicitly.</summary>
    [Fact]
    public void UnsupportedDeviceDoesNotClaimMeshSupport()
    {
        using var device = new Direct3D12Device(new FakeQueueDriver());
        Assert.Equal((GraphicsFeatures)0, device.Caps.Features & (GraphicsFeatures.MeshShader | GraphicsFeatures.TaskShader));
        Assert.Null(device.Caps.MeshShaderLimits);
        Assert.Null(device.Caps.TaskShaderLimits);
        Assert.Throws<NotSupportedException>(() => device.CreateMeshPipeline(new() { MeshShader = null! }));
    }

    /// <summary>Mesh and Task shader reads are valid dependency access classes.</summary>
    [Fact]
    public void MeshShaderReadScopesAreAccepted()
    {
        CommandValidation.Scope(new(PipelineStage.MeshShader, ResourceAccess.ShaderRead));
        CommandValidation.Scope(new(PipelineStage.TaskShader, ResourceAccess.ShaderRead));
        Assert.Throws<ArgumentException>(() => CommandValidation.Scope(new(PipelineStage.MeshShader, ResourceAccess.IndexRead)));
    }

    /// <summary>D3D12 block-rounded output memory is checked independently of portable byte counts.</summary>
    [Fact]
    public void NativeOutputRoundingIsCheckedBeforePsoCreation()
    {
        using var document = JsonDocument.Parse("""
            { "maxOutputVertices": 129, "maxOutputPrimitives": 1, "payloadBytes": 0,
              "outputs": [
                { "type": "float4", "perPrimitive": false },
                { "type": "float4", "perPrimitive": false },
                { "type": "float4", "perPrimitive": false },
                { "type": "float4", "perPrimitive": false },
                { "type": "float4", "perPrimitive": false },
                { "type": "float4", "perPrimitive": false },
                { "type": "float4", "perPrimitive": false },
                { "type": "float4", "perPrimitive": false },
                { "type": "float4", "perPrimitive": false },
                { "type": "float4", "perPrimitive": false },
                { "type": "float4", "perPrimitive": false },
                { "type": "float4", "perPrimitive": false },
                { "type": "float4", "perPrimitive": false }
              ] }
            """);
        Assert.Throws<NotSupportedException>(() => Direct3D12MeshPipeline.ValidateNativeOutput(document.RootElement));
    }

    /// <summary>Executes a mesh PSO on explicitly enabled hardware and verifies rasterized pixels.</summary>
    /// <param name="useTask">Whether an amplification shader launches the mesh group.</param>
    /// <returns>The asynchronous GPU readback verification.</returns>
    [Trait("Execution", "Gpu")]
    [AgilityTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HardwareMeshTriangleAsync(bool useTask)
    {
        using var device = Direct3D12Device.Create(new() { EnableDebugLayer = true });
        Assert.True((device.Caps.Features & GraphicsFeatures.MeshShader) != 0, "This opt-in test requires mesh-capable hardware, not WARP.");
        const string Source = """
            struct Vertex { float4 position : SV_Position; };
            [shader("mesh")][numthreads(1,1,1)][outputtopology("triangle")]
            void main(out vertices Vertex vertices[3], out indices uint3 triangles[1])
            {
                SetMeshOutputCounts(3,1);
                vertices[0].position=float4(-1,-1,0,1);
                vertices[1].position=float4(-1,3,0,1);
                vertices[2].position=float4(3,-1,0,1);
                triangles[0]=uint3(0,1,2);
            }
            """;
        var compiler = new SlangShaderCompiler();
        string meshSource = useTask ? "struct Payload { uint value; };\n" + Source.Replace("void main(out vertices", "void main(in payload Payload payload, out vertices", StringComparison.Ordinal).Replace("SetMeshOutputCounts(3,1);", "SetMeshOutputCounts(payload.value == 1 ? 3 : 0,1);", StringComparison.Ordinal) : Source;
        using IGraphicsShader? task = useTask ? device.CreateShader(await compiler.CompileAsync(new() { Source = "struct Payload { uint value; }; groupshared Payload payload; [shader(\"amplification\")][numthreads(1,1,1)] void main() { payload.value=1; DispatchMesh(1,1,1,payload); }", Stage = ShaderStage.Task, Target = ShaderTarget.Dxil })) : null;
        using IGraphicsShader mesh = device.CreateShader(await compiler.CompileAsync(new() { Source = meshSource, Stage = ShaderStage.Mesh, Target = ShaderTarget.Dxil }));
        using IGraphicsShader fragment = device.CreateShader(await compiler.CompileAsync(new() { Source = "float4 main() : SV_Target { return float4(1,0,0,1); }", Stage = ShaderStage.Fragment, Target = ShaderTarget.Dxil }));
        using IMeshPipeline pipeline = device.CreateMeshPipeline(new() { MeshShader = mesh, TaskShader = task, FragmentShader = fragment });
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 4, Height = 4, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = 1024, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, Before = default, AfterState = TextureState.ColorAttachment, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
        render.SetPipeline(pipeline);
        render.SetRenderState(new() { ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } });
        render.SetViewport(new(0, 0, 4, 4, 0, 1));
        render.SetScissor(new(0, 0, 4, 4));
        render.SetBlendConstant(new(0, 0, 0, 0));
        render.SetStencilReference(0);
        Assert.Throws<InvalidOperationException>(() => render.Draw(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => render.DrawMeshTasks(0));
        render.DrawMeshTasks(1);
        render.End();
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyTextureToBuffer(new() { Texture = texture, Width = 4, Height = 4 }, new() { Buffer = readback.SliceElements(0, 1024), BytesPerRow = 256, RowsPerImage = 4 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, 1024), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] pixels = new byte[1024];
        readback.CopyTo(pixels);
        readback.Unmap();
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 4; x++)
            {
                Assert.Equal(new byte[] { 255, 0, 0, 255 }, pixels.AsSpan((y * 256) + (x * 4), 4).ToArray());
            }
        }
    }

    private sealed class FakeQueueDriver : IDirect3D12QueueDriver
    {
        public void CreateFence(Direct3D12Device owner, out nint fence) => fence = 0;

        public void Wait(Direct3D12Device owner, nint fence, ulong value) => throw new NotSupportedException();

        public void Signal(Direct3D12Device owner, nint fence, ulong value) => throw new NotSupportedException();

        public void Execute(Direct3D12Device owner, ReadOnlySpan<nint> commands) => throw new NotSupportedException();

        public void SignalCompletion(Direct3D12Device owner, nint fence) => throw new NotSupportedException();

        public ulong Completed(nint fence) => 0;

        public int DeviceRemoved(Direct3D12Device owner) => 0;

        public Task WaitForCompletion(Direct3D12Device owner, nint fence) => throw new NotSupportedException();

        public void Release(nint fence)
        {
        }
    }
}
