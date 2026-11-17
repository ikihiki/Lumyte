using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Validation;
using Lumyte.Graphics.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks native Mesh/Task rasterization and ordinary-program transitions by pixel readback.</summary>
public sealed class MeshGpuTests
{
    private static string MeshSource => """
        #include "lumyte.slang"
        struct MeshBufferArguments { GpuBufferRef<uint> Buffer; };
        ConstantBuffer<MeshBufferArguments> arguments;
        struct Vertex { float4 position : SV_Position; };
        [shader("mesh")] [numthreads(1,1,1)] [outputtopology("triangle")]
        void main(out vertices Vertex vertices[3], out indices uint3 triangles[1])
        {
            uint enabled = arguments.Buffer.Load(0) == 1 ? 1 : 0;
            SetMeshOutputCounts(3*enabled,enabled);
            vertices[0].position=float4(-1,-1,0,1);
            vertices[1].position=float4(3,-1,0,1);
            vertices[2].position=float4(-1,3,0,1);
            triangles[0]=uint3(0,1,2);
        }
        """;

    private static string ComputeSource => """
        #include "lumyte.slang"
        struct MeshBufferArguments { GpuRWBufferRef<uint> Buffer; };
        ConstantBuffer<MeshBufferArguments> arguments;
        [shader("compute")] [numthreads(1,1,1)]
        void main() { arguments.Buffer.Store(0,1); }
        """;

    private static string TaskSource => """
        #include "lumyte.slang"
        struct MeshBufferArguments { GpuBufferRef<uint> Buffer; };
        ConstantBuffer<MeshBufferArguments> arguments;
        struct Payload { uint value; }; groupshared Payload payload;
        [shader("amplification")] [numthreads(1,1,1)]
        void main() { payload.value=arguments.Buffer.Load(0); DispatchMesh(1,1,1,payload); }
        """;

    /// <summary>Runs only when the selected Vulkan device really enables mesh shaders.</summary>
    /// <returns>The native rendering verification.</returns>
    [MeshGpuFact]
    public async Task VulkanMeshRenderAndSwitchAsync() => await CheckAsync();

    /// <summary>Checks stencil rejection only when the selected driver supports the portable depth/stencil format.</summary>
    /// <returns>The native stencil pixel verification.</returns>
    [MeshStencilGpuFact]
    public async Task VulkanMeshStencilAsync() => await CheckAsync(stencilOnly: true);

    /// <summary>Checks real Task payload production and resource barriers independently of optional Task availability.</summary>
    /// <returns>The native Task-to-Mesh pixel verification.</returns>
    [MeshTaskGpuFact]
    public async Task VulkanTaskPayloadAndBarrierAsync() => await CheckAsync(taskRequired: true);

    private static async Task CheckAsync(bool stencilOnly = false, bool taskRequired = false)
    {
        using var raw = VulkanDevice.Create();
        Assert.True((raw.Caps.Features & GraphicsFeatures.MeshShader) != 0);
        if (taskRequired)
        {
            Assert.True((raw.Caps.Features & GraphicsFeatures.TaskShader) != 0);
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var compiler = new SlangShaderCompiler();
        ShaderArtifact mesh = await compiler.CompileAsync(new() { Source = MeshSource, Stage = ShaderStage.Mesh, Target = ShaderTarget.SpirV }, timeout.Token);
        ShaderArtifact fragment = await compiler.CompileAsync(new() { Source = "float4 main() : SV_Target0 { return float4(1,0,0,1); }", Stage = ShaderStage.Fragment, Target = ShaderTarget.SpirV }, timeout.Token);
        ShaderArtifact vertex = await compiler.CompileAsync(new() { Source = "float4 main(uint i : SV_VertexID) : SV_Position { return float4(i == 0 ? float2(-1,-1) : (i == 1 ? float2(3,-1) : float2(-1,3)), 0, 1); }", Stage = ShaderStage.Vertex, Target = ShaderTarget.SpirV }, timeout.Token);
        ShaderArtifact green = await compiler.CompileAsync(new() { Source = "float4 main() : SV_Target0 { return float4(0,1,0,1); }", Stage = ShaderStage.Fragment, Target = ShaderTarget.SpirV }, timeout.Token);
        foreach (bool validated in new[] { false, true })
        {
            using ValidationDevice? layer = validated ? new(raw) : null;
            IGraphicDevice device = layer ?? (IGraphicDevice)raw;
            using IGraphicsShader ms = device.CreateShader(mesh);
            using IGraphicsShader ps = device.CreateShader(fragment);
            using IGraphicsShader vs = device.CreateShader(vertex);
            using IGraphicsShader greenShader = device.CreateShader(green);
            Assert.Throws<ArgumentException>(() => device.CreateMeshPipeline(new() { MeshShader = vs, FragmentShader = ps }));
            Assert.Throws<ArgumentException>(() => device.CreateMeshPipeline(new() { MeshShader = ms, FragmentShader = ms }));
            IGraphicsShader disposedShader = device.CreateShader(mesh);
            disposedShader.Dispose();
            Assert.Throws<ObjectDisposedException>(() => device.CreateMeshPipeline(new() { MeshShader = disposedShader }));
            using IMeshPipeline pipeline = device.CreateMeshPipeline(new() { MeshShader = ms, FragmentShader = ps });
            using IGraphicsPipeline ordinary = device.CreateGraphicsPipeline(new() { VertexShader = vs, FragmentShader = greenShader });
            await RenderCheckedAsync(device, pipeline, ordinary, timeout.Token);
            await RenderCheckedAsync(device, pipeline, ordinary, timeout.Token, computeWrite: true);
            for (int stateMode = stencilOnly ? 3 : 1; stateMode <= (stencilOnly ? 3 : 2); stateMode++)
            {
                await RenderCheckedAsync(device, pipeline, ordinary, timeout.Token, stateMode: stateMode);
            }

            using IMeshPipeline noFragment = device.CreateMeshPipeline(new() { MeshShader = ms });
            await RenderCheckedAsync(device, noFragment, ordinary, timeout.Token);
            string lineSource = MeshSource.Replace("triangle", "line", StringComparison.Ordinal).Replace("vertices[3]", "vertices[2]", StringComparison.Ordinal).Replace("uint3 lines", "uint2 lines", StringComparison.Ordinal).Replace("3*enabled", "2*enabled", StringComparison.Ordinal).Replace("float4(-1,-1,0,1)", "float4(-1,0,0,1)", StringComparison.Ordinal).Replace("float4(3,-1,0,1)", "float4(3,0,0,1)", StringComparison.Ordinal).Replace("vertices[2].position=float4(-1,3,0,1);", string.Empty, StringComparison.Ordinal).Replace("uint3(0,1,2)", "uint2(0,1)", StringComparison.Ordinal);
            ShaderArtifact lineArtifact = await compiler.CompileAsync(new() { Source = lineSource, Stage = ShaderStage.Mesh, Target = ShaderTarget.SpirV }, timeout.Token);
            using IGraphicsShader lineShader = device.CreateShader(lineArtifact);
            using IMeshPipeline line = device.CreateMeshPipeline(new() { MeshShader = lineShader, FragmentShader = ps });
            await RenderCheckedAsync(device, line, ordinary, timeout.Token);
            if (taskRequired)
            {
                ShaderArtifact payloadMesh = await compiler.CompileAsync(new() { Source = "struct Payload { uint value; };\n" + MeshSource.Replace("void main(", "void main(in payload Payload p,", StringComparison.Ordinal).Replace("uint enabled = arguments.Buffer.Load(0) == 1 ? 1 : 0;", "uint enabled = arguments.Buffer.Load(0) == 1 && p.value == 1 ? 1 : 0;", StringComparison.Ordinal), Stage = ShaderStage.Mesh, Target = ShaderTarget.SpirV }, timeout.Token);
                ShaderArtifact task = await compiler.CompileAsync(new() { Source = TaskSource, Stage = ShaderStage.Task, Target = ShaderTarget.SpirV }, timeout.Token);
                using IGraphicsShader t = device.CreateShader(task);
                using IGraphicsShader m = device.CreateShader(payloadMesh);
                using IMeshPipeline taskPipeline = device.CreateMeshPipeline(new() { MeshShader = m, TaskShader = t, FragmentShader = ps });
                await RenderCheckedAsync(device, taskPipeline, ordinary, timeout.Token);
                await RenderCheckedAsync(device, taskPipeline, ordinary, timeout.Token, computeWrite: true);
            }
        }
    }

    private static async Task RenderCheckedAsync(IGraphicDevice device, IMeshPipeline pipeline, IGraphicsPipeline ordinary, CancellationToken cancellationToken, bool computeWrite = false, int stateMode = 0)
    {
        try
        {
            await RenderAsync(device, pipeline, ordinary, cancellationToken, computeWrite, stateMode);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            throw;
        }
    }

    private static async Task RenderAsync(IGraphicDevice device, IMeshPipeline pipeline, IGraphicsPipeline ordinary, CancellationToken cancellationToken, bool computeWrite = false, int stateMode = 0)
    {
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsTexture? depth = stateMode >= 2 ? device.CreateTexture(new() { Width = 1, Height = 1, Format = stateMode == 3 ? TextureFormat.Depth24Stencil8 : TextureFormat.Depth32Float, Usage = TextureUsage.RenderAttachment }) : null;
        using IGraphicsTextureView? depthView = depth?.CreateView();
        TextureCopyLayout layout = device.GetTextureCopyLayout(texture.Format);
        uint pitch = ((4 + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsBuffer<uint> upload = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        using IGraphicsBuffer<uint> input = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead | BufferUsage.ShaderWrite });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 1 });
        var arguments = new MeshBufferArguments(table.WriteBuffer(0, input.SliceElements(0, 1)));
        using IArgumentTable computeTable = device.CreateArgumentTable(new() { BufferCapacity = 1 });
        var computeArguments = new MeshBufferArguments(computeTable.WriteBuffer(0, input.SliceElements(0, 1), ShaderBufferAccess.ReadWrite));
        ShaderArtifact? computeArtifact = computeWrite ? await new SlangShaderCompiler().CompileAsync(new() { Source = ComputeSource, Stage = ShaderStage.Compute, Target = ShaderTarget.SpirV }, cancellationToken) : null;
        using IGraphicsShader? computeShader = computeArtifact == null ? null : device.CreateShader(computeArtifact);
        using IGraphicsComputePipeline? computePipeline = computeShader == null ? null : device.CreateComputePipeline(new() { ComputeShader = computeShader });
        await upload.MapAsync(cancellationToken);
        upload.CopyFrom(new uint[] { computeWrite ? 0u : 1u });
        upload.Unmap();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = upload.SliceElements(0, 1), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(upload.SliceElements(0, 1), input.SliceElements(0, 1));
        PipelineStage stages = PipelineStage.MeshShader | (pipeline.Desc.TaskShader == null ? PipelineStage.None : PipelineStage.TaskShader);
        if (computePipeline != null)
        {
            commands.Barrier(new BufferBarrierDesc<uint> { Buffer = input.SliceElements(0, 1), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite) });
            IComputeEncoder compute = commands.BeginComputePass(new());
            compute.SetPipeline(computePipeline);
            compute.SetArgumentTable(computeTable);
            compute.SetArguments(in computeArguments);
            compute.Dispatch(1);
            compute.End();
        }

        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = input.SliceElements(0, 1), Before = computePipeline == null ? new(PipelineStage.Copy, ResourceAccess.CopyWrite) : new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(stages, ResourceAccess.ShaderRead) });
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), Before = default, BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
        if (depth != null)
        {
            commands.Barrier(new TextureBarrierDesc { Texture = depth, Range = new(0, 1, 0, 1), Before = default, BeforeState = TextureState.Undefined, AfterState = TextureState.DepthStencilAttachment, After = new(PipelineStage.DepthStencil, ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite) });
        }

        IRenderEncoder render = commands.BeginRenderPass(new() { DepthStencilAttachment = depthView == null ? null : new() { View = depthView }, ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
        PrimitiveTopology topology = MeshShaderMetadata.Read(pipeline.Desc.MeshShader.Artifact.GetTarget(ShaderTarget.SpirV)).GetProperty("outputTopology").GetString() == "line" ? PrimitiveTopology.LineList : PrimitiveTopology.TriangleList;
        var state = new GraphicsRenderStateDesc { Topology = topology, ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } };
        if (stateMode == 1)
        {
            state = state with { ColorTargets = [new() { BlendEnable = true, Color = new() { Source = BlendFactor.One, Destination = BlendFactor.One }, Alpha = new() { Source = BlendFactor.One, Destination = BlendFactor.Zero } }] };
        }
        else if (stateMode == 2)
        {
            state = state with { DepthStencil = new() { DepthTestEnable = true, DepthCompare = CompareFunction.Never } };
        }
        else if (stateMode == 3)
        {
            state = state with { DepthStencil = new() { StencilTestEnable = true, Front = new() { Compare = CompareFunction.Equal }, Back = new() { Compare = CompareFunction.Equal } } };
        }

        render.SetRenderState(state);
        render.SetViewport(new(0, 0, 1, 1, 0, 1));
        render.SetScissor(new(0, 0, 1, 1));
        render.SetBlendConstant(new(0, 0, 0, 0));
        render.SetStencilReference(stateMode == 3 ? 1u : 0u);
        render.SetPipeline(pipeline);
        render.SetArgumentTable(table);
        render.SetArguments(in arguments);
        Assert.Throws<InvalidOperationException>(() => render.Draw(3));
        Assert.Throws<InvalidOperationException>(() => render.DrawIndexed(0));
        Assert.Throws<InvalidOperationException>(() => render.DrawIndirect(default));
        Assert.Throws<InvalidOperationException>(() => render.DrawIndexedIndirect(default));
        Assert.Throws<ArgumentOutOfRangeException>(() => render.DrawMeshTasks(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => render.DrawMeshTasks(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => render.DrawMeshTasks(1, 1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => render.DrawMeshTasks(uint.MaxValue, uint.MaxValue, uint.MaxValue));
        render.DrawMeshTasks(1);
        render.SetRenderState(state with { Topology = PrimitiveTopology.TriangleList, ColorTargets = [new()], DepthStencil = new() });
        render.SetPipeline(ordinary);
        Assert.Throws<InvalidOperationException>(() => render.DrawMeshTasks(1));
        render.Draw(3);
        render.SetRenderState(state);
        render.SetPipeline(pipeline);
        render.SetArgumentTable(table);
        render.SetArguments(in arguments);
        render.DrawMeshTasks(1);
        render.End();
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyTextureToBuffer(new() { Texture = texture, Width = 1, Height = 1 }, new() { Buffer = readback.SliceElements(0, pitch), BytesPerRow = pitch, RowsPerImage = 1 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, pitch), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync(cancellationToken);
        await readback.MapAsync(cancellationToken);
        byte[] bytes = new byte[pitch];
        readback.CopyTo(bytes);
        readback.Unmap();
        byte[] expected = pipeline.Desc.FragmentShader == null || stateMode >= 2 ? [0, 255, 0, 255] : stateMode == 1 ? [255, 255, 0, 255] : [255, 0, 0, 255];
        Assert.Equal(expected, bytes[..4]);
    }
}
