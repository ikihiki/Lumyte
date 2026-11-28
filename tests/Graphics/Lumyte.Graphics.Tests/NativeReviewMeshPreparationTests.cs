using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises preparation when only the Task stage declares the application root.</summary>
public sealed class NativeReviewMeshPreparationTests
{
    /// <summary>Prepared Task bytes determine dispatch and survive through a real Mesh pixel readback.</summary>
    /// <returns>The completed hardware check.</returns>
    [MeshTaskGpuFact]
    [Trait("Execution", "Gpu")]
    public async Task TaskOnlyRootPreparesAndRendersAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var device = VulkanDevice.Create();
        var compiler = new SlangShaderCompiler();
        const string TaskSource = """
            struct NativeReviewTaskRoot { uint Value; }; ConstantBuffer<NativeReviewTaskRoot> arguments;
            struct Payload { uint value; }; groupshared Payload payload;
            [shader("amplification")] [numthreads(1,1,1)]
            void main() { payload.value=arguments.Value; DispatchMesh(arguments.Value == 17 ? 1 : 0,1,1,payload); }
            """;
        const string MeshSource = """
            struct Payload { uint value; }; struct Vertex { float4 position : SV_Position; };
            [shader("mesh")] [numthreads(1,1,1)] [outputtopology("triangle")]
            void main(in payload Payload payload, out vertices Vertex vertices[3], out indices uint3 triangles[1])
            {
                SetMeshOutputCounts(payload.value == 17 ? 3 : 0,payload.value == 17 ? 1 : 0);
                vertices[0].position=float4(-1,-1,0,1); vertices[1].position=float4(3,-1,0,1); vertices[2].position=float4(-1,3,0,1);
                triangles[0]=uint3(0,1,2);
            }
            """;
        ShaderArtifact task = await compiler.CompileAsync(new() { Source = TaskSource, Stage = ShaderStage.Task, Target = ShaderTarget.SpirV }, timeout.Token);
        ShaderArtifact mesh = await compiler.CompileAsync(new() { Source = MeshSource, Stage = ShaderStage.Mesh, Target = ShaderTarget.SpirV }, timeout.Token);
        ShaderArtifact fragment = await compiler.CompileAsync(new() { Source = "float4 main() : SV_Target { return float4(1,0,0,1); }", Stage = ShaderStage.Fragment, Target = ShaderTarget.SpirV }, timeout.Token);
        using IGraphicsShader ts = device.CreateShader(task);
        using IGraphicsShader ms = device.CreateShader(mesh);
        using IGraphicsShader fs = device.CreateShader(fragment);
        using IMeshPipeline pipeline = device.CreateMeshPipeline(new() { TaskShader = ts, MeshShader = ms, FragmentShader = fs });
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = texture.CreateView();
        uint pitch = device.GetTextureCopyLayout(TextureFormat.Rgba8Unorm).BytesPerRowAlignment;
        pitch = checked(((4 + pitch - 1) / pitch) * pitch);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        var state = new GraphicsRenderStateDesc { ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } };
        var pass = new RenderPassDesc { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] };
        var root = new TaskRoot(new object());
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.PrepareMesh(pass, pipeline, state, null, in root);
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
        IRenderEncoder encoder = commands.BeginRenderPass(pass);
        encoder.SetPipeline(pipeline);
        encoder.SetRenderState(state);
        encoder.SetViewport(new(0, 0, 1, 1, 0, 1));
        encoder.SetScissor(new(0, 0, 1, 1));
        encoder.SetBlendConstant(default);
        encoder.SetStencilReference(0);
        encoder.SetArguments(in root);
        encoder.DrawMeshTasks(1);
        encoder.End();
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyTextureToBuffer(new() { Texture = texture, Width = 1, Height = 1 }, new() { Buffer = readback.SliceElements(0, pitch), BytesPerRow = pitch, RowsPerImage = 1 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, pitch), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync(timeout.Token);
        await readback.MapAsync(timeout.Token);
        byte[] actual = new byte[pitch];
        readback.CopyTo(actual);
        readback.Unmap();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, actual[..4]);
    }

    private readonly struct TaskRoot(object identity) : IShaderArguments
    {
        public static string ShaderTypeName => "NativeReviewTaskRoot";

        public static int TransferSize => 4;

        public static int ReferenceCount => 0;

        public object NativePreparationIdentity => identity;

        public void Write<TWriter>(ref TWriter writer)
            where TWriter : IShaderValueWriter, allows ref struct
        {
            uint value = 17;
            writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
        }
    }
}
