using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Exercises generated arguments, cyclic CPU dependencies and GPU binding through common APIs.</summary>
public static class ShaderBindingExercise
{
    /// <summary>Checks matrix root values, per-dispatch snapshots and independently registered textures.</summary>
    /// <param name="device">The backend device created by the sample bootstrap.</param>
    /// <returns>The report after GPU readback matches expected values.</returns>
    public static async Task<string> RunAsync(IGraphicDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        await CheckRegistrationIdentityAsync(device);
        await CheckExplicitTransferRequiredAsync(device);
        await ComputeAsync(device);
        await DrawAsync(device);
        await CameraAsync(device);
        await BindingRegressionExercise.RunAsync(device);
        return "Shader binding checks passed: matrix root, cyclic dependencies, explicit staging transfers, twenty distinct textures, shader layouts and registered buffer ranges.";
    }

    /// <summary>Checks the same CPU-set data contract with a backend-only online artifact.</summary>
    /// <param name="device">The already created device.</param>
    /// <param name="compiler">The online compiler selected by the application bootstrap.</param>
    /// <returns>The asynchronous verification.</returns>
    public static async Task RunOnlineAsync(IGraphicDevice device, IShaderCompiler compiler)
    {
        using Stream source = typeof(ShaderBindingExercise).Assembly.GetManifestResourceStream("Lumyte.Shaders.binding-compute.slang")!;
        using var reader = new StreamReader(source);
        ShaderArtifact artifact = await compiler.CompileAsync(new() { Source = await reader.ReadToEndAsync(), Stage = ShaderStage.Compute, Target = device.Caps.ShaderTarget });
        await ComputeAsync(device, artifact);
    }

    private static ShaderArtifact Artifact(string name) => ShaderArtifact.LoadEmbedded(typeof(ShaderBindingExercise).Assembly, "Lumyte.Shaders." + name + ".lshader");

    private static async Task CheckRegistrationIdentityAsync(IGraphicDevice device)
    {
        ShaderArtifact artifact = Artifact("binding-compute");
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsShaderDataBuffer<BindingNode> nodes = device.CreateBuffer<BindingNode>(artifact, 1);
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.ShaderWrite });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 2 });
        IGpuRef<BindingNode> node = table.WriteBuffer(0, nodes.SliceElements(0, 1));
        IGpuRef<uint> result = table.WriteBuffer(1, output.Slice(0, 1));
        using IGraphicsShaderDataBuffer<BindingNode> staging = device.CreateBuffer<BindingNode>(artifact, 1, MemoryPreference.Upload);
        await staging.MapAsync();
        staging.CopyFrom([new(1, node)]);
        staging.Unmap();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        RecordUpload(commands, staging, nodes);
        IComputeEncoder compute = commands.BeginComputePass(new());
        compute.SetPipeline(pipeline);
        compute.SetArgumentTable(table);
        var arguments = new BindingComputeArguments(Matrix4x4.Identity, 0, node, result);
        compute.SetArguments(in arguments);
        compute.Dispatch(1);
        compute.End();
        commands.Finish();
        table.ReleaseBuffer(1);
        _ = table.WriteBuffer(1, output.Slice(0, 1));
        try
        {
            using IGraphicsSubmission unexpected = device.Queue.Submit([commands]);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException("Replacing a slot allowed a recorded reference to resolve to the new registration.");
    }

    private static async Task ComputeAsync(IGraphicDevice device, ShaderArtifact? sourceArtifact = null)
    {
        ShaderArtifact artifact = sourceArtifact ?? Artifact("binding-compute");
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsShaderDataBuffer<BindingNode> nodes = device.CreateBuffer<BindingNode>(artifact, 2);
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 8, Usage = BufferUsage.ShaderWrite | BufferUsage.CopySource });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 8, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 2 });
        IGpuRef<BindingNode> node = table.WriteBuffer(0, nodes.SliceElements(0, 2));
        IGpuRef<uint> results = table.WriteBuffer(1, output.Slice(0, 8));
        using IGraphicsShaderDataBuffer<BindingNode> staging = device.CreateBuffer<BindingNode>(artifact, 2, MemoryPreference.Upload);
        using IGraphicsShaderDataBuffer<BindingNode> changed = device.CreateBuffer<BindingNode>(artifact, 1, MemoryPreference.Upload);
        await staging.MapAsync();
        staging.CopyFrom([new(3, node.GetElement(1)), new(7, node.GetElement(0))]);
        staging.Unmap();
        await changed.MapAsync();
        changed.CopyFrom([new(9, node.GetElement(1))]);
        changed.Unmap();
        Require(nodes.Count == 2 && nodes.SizeInBytes == nodes.ShaderElementStrideInBytes * 2, "Shader data count or size changed.");
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        RecordUpload(commands, staging, nodes);
        IComputeEncoder compute = commands.BeginComputePass(new());
        compute.SetPipeline(pipeline);
        compute.SetArgumentTable(table);
        var first = new BindingComputeArguments(Matrix4x4.CreateScale(3), 20, node.GetElement(0), results.GetElement(3));
        compute.SetArguments(in first);
        compute.Dispatch(1);
        compute.End();
        commands.Barrier(new ShaderDataBufferBarrierDesc<BindingNode> { Buffer = nodes.SliceElements(0, 1), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderRead), After = new(PipelineStage.Copy, ResourceAccess.CopyWrite) });
        RecordUpload(commands, changed, nodes, 1);
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = output.Slice(0, 8), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite) });
        compute = commands.BeginComputePass(new());
        compute.SetPipeline(pipeline);
        compute.SetArgumentTable(table);
        BindingComputeArguments second = first with { Output = results.GetElement(4) };
        compute.SetArguments(in second);
        compute.Dispatch(1);
        compute.End();
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = output.Slice(0, 8), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(output.Slice(0, 8), readback.Slice(0, 8));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.Slice(0, 8), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        uint[] values = new uint[8];
        readback.CopyTo(values);
        readback.Unmap();
        Require(values[3] == 36 && values[4] == 48, "Root matrix, recursive references or explicit staging copies are incorrect.");
    }

    private static async Task DrawAsync(IGraphicDevice device)
    {
        const int Count = 20;
        const uint Width = Count * 4;
        ShaderArtifact artifact = Artifact("binding-fragment");
        using IGraphicsShader vertex = device.CreateShader(Artifact("fullscreen-vertex"));
        using IGraphicsShader fragment = device.CreateShader(artifact);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using IGraphicsShaderDataBuffer<BindingMaterial> materials = device.CreateBuffer<BindingMaterial>(artifact, Count);
        using IGraphicsShaderDataBuffer<BindingMaterial> staging = device.CreateBuffer<BindingMaterial>(artifact, Count, MemoryPreference.Upload);
        using IGraphicsSampler sampler = device.CreateSampler(new());
        using IGraphicsTexture target = device.CreateTexture(new() { Width = Width, Height = 4, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView targetView = target.CreateView();
        uint alignment = device.GetTextureCopyLayout(target.Format).BytesPerRowAlignment;
        uint pitch = checked((((Width * 4) + alignment - 1) / alignment) * alignment);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        var textures = new List<IGraphicsTexture>();
        var views = new List<IGraphicsTextureView>();
        using IArgumentTable table = device.CreateArgumentTable(new() { TextureCapacity = Count, SamplerCapacity = 1, BufferCapacity = 2 });
        try
        {
            IGpuRef<IGraphicsSampler> samplerRef = table.WriteSampler(0, sampler);
            var values = new BindingMaterial[Count];
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            var colorWrite = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
            for (int i = 0; i < Count; i++)
            {
                IGraphicsTexture texture = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled });
                textures.Add(texture);
                IGraphicsTextureView view = texture.CreateView();
                views.Add(view);
                values[i] = new(Vector4.One, table.WriteTexture((uint)i, view), samplerRef);
                commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, Before = default, AfterState = TextureState.ColorAttachment, After = colorWrite });
                IRenderEncoder clear = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new((i + 1) / 255f, (Count - i) / 255f, (i * 7) / 255f, 1) }] });
                clear.End();
                commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.Sampled, Before = colorWrite, After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
            }

            await staging.MapAsync();
            staging.CopyFrom(values);
            staging.Unmap();
            RecordUpload(commands, staging, materials, Count, PipelineStage.FragmentShader);
            IGpuRef<BindingMaterial> materialRef = table.WriteBuffer(0, materials.SliceElements(0, Count));
            commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, Before = default, AfterState = TextureState.ColorAttachment, After = colorWrite });
            IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = targetView, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
            render.SetPipeline(pipeline);
            render.SetArgumentTable(table);
            render.SetRenderState(new() { ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } });
            render.SetViewport(new(0, 0, Width, 4, 0, 1));
            render.SetBlendConstant(new(0, 0, 0, 0));
            render.SetStencilReference(0);

            // One draw reaches five independent textures sharing one sampler, exceeding the old four-pair limit.
            var rangeArguments = new BindingDrawArguments(table.WriteBuffer(1, materials.SliceElements(0, 5)), 5);
            render.SetArguments(in rangeArguments);
            render.SetScissor(new(0, 0, 20, 4));
            render.Draw(3);
            for (int i = 5; i < Count; i++)
            {
                // Select one element per primitive; the backend collects only this material's dependencies.
                var arguments = new BindingDrawArguments(materialRef.GetElement((ulong)i), 1);
                render.SetArguments(in arguments);
                render.SetScissor(new((uint)i * 4, 0, 4, 4));
                render.Draw(3);
            }

            render.End();
            commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = colorWrite, After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
            commands.CopyTextureToBuffer(new() { Texture = target, Width = Width, Height = 4 }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 4 });
            commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
            commands.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            await submission.WaitAsync();
            await readback.MapAsync();
            byte[] bytes = new byte[checked((int)readback.Count)];
            readback.CopyTo(bytes);
            readback.Unmap();
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    int index = x / 4;
                    int offset = checked(((int)pitch * y) + (x * 4));
                    Require(bytes[offset] == index + 1 && bytes[offset + 1] == Count - index && bytes[offset + 2] == index * 7 && bytes[offset + 3] == 255, "Twenty-texture readback differs from the material colors.");
                }
            }
        }
        finally
        {
            table.Dispose();
            foreach (IGraphicsTextureView view in views)
            {
                view.Dispose();
            }

            foreach (IGraphicsTexture texture in textures)
            {
                texture.Dispose();
            }
        }
    }

    private static async Task CameraAsync(IGraphicDevice device)
    {
        using IGraphicsShader vertex = device.CreateShader(Artifact("fullscreen-vertex"));
        using IGraphicsShader fragment = device.CreateShader(Artifact("binding-camera"));
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = target.CreateView();
        uint pitch = Math.Max(4, device.GetTextureCopyLayout(target.Format).BytesPerRowAlignment);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var write = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = write });
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
        render.SetPipeline(pipeline);
        var arguments = new BindingCameraArguments(Matrix4x4.CreateScale(0.25f, 0.5f, 0.75f));
        render.SetArguments(in arguments);
        render.SetRenderState(new() { ColorTargets = [new()], Rasterization = new() { Cull = CullMode.None } });
        render.SetViewport(new(0, 0, 1, 1, 0, 1));
        render.SetScissor(new(0, 0, 1, 1));
        render.SetBlendConstant(new(0, 0, 0, 0));
        render.SetStencilReference(0);
        render.Draw(3);
        render.End();
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = write, After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyTextureToBuffer(new() { Texture = target, Width = 1, Height = 1 }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 1 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] bytes = new byte[checked((int)readback.Count)];
        readback.CopyTo(bytes);
        readback.Unmap();
        Require(Math.Abs(bytes[0] - 64) <= 1 && Math.Abs(bytes[1] - 128) <= 1 && Math.Abs(bytes[2] - 191) <= 1 && bytes[3] == 255, "Numeric root arguments require an unnecessary table or have an incorrect matrix layout.");
    }

    private static void RecordUpload<T>(IGraphicsCommandBuffer commands, IGraphicsShaderDataBuffer<T> staging, IGraphicsShaderDataBuffer<T> gpu, ulong? count = null, PipelineStage stage = PipelineStage.ComputeShader)
        where T : struct, IShaderData
    {
        ulong length = count ?? staging.Count;
        commands.Barrier(new ShaderDataBufferBarrierDesc<T> { Buffer = staging.SliceElements(0, length), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(staging.SliceElements(0, length), gpu.SliceElements(0, length));
        commands.Barrier(new ShaderDataBufferBarrierDesc<T> { Buffer = gpu.SliceElements(0, length), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(stage, ResourceAccess.ShaderRead) });
    }

    private static async Task CheckExplicitTransferRequiredAsync(IGraphicDevice device)
    {
        ShaderArtifact artifact = Artifact("binding-compute");
        using IGraphicsShader shader = device.CreateShader(artifact);
        using IGraphicsComputePipeline pipeline = device.CreateComputePipeline(new() { ComputeShader = shader });
        using IGraphicsShaderDataBuffer<BindingNode> nodes = device.CreateBuffer<BindingNode>(artifact, 1);
        using IGraphicsShaderDataBuffer<BindingNode> staging = device.CreateBuffer<BindingNode>(artifact, 1, MemoryPreference.Upload);
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 1, Usage = BufferUsage.ShaderWrite });
        using IArgumentTable table = device.CreateArgumentTable(new() { BufferCapacity = 2 });
        IGpuRef<BindingNode> node = table.WriteBuffer(0, nodes.SliceElements(0, 1));
        IGpuRef<uint> result = table.WriteBuffer(1, output.Slice(0, 1));
        RequireThrows(() => nodes.CopyFrom([new(1, node)]), "GPU storage accepted a CPU value write.");
        RequireThrows(() => staging.CopyFrom([new(1, node)]), "Unmapped staging accepted a CPU value write.");
        await staging.MapAsync();
        staging.CopyFrom([new(1, node)]);
        staging.Unmap();
        using (IGraphicsCommandBuffer discarded = device.CreateCommandBuffer(new()))
        {
            RecordUpload(discarded, staging, nodes);
            discarded.Finish();
        }

        using (IGraphicsCommandBuffer stale = device.CreateCommandBuffer(new()))
        {
            RecordUpload(stale, staging, nodes);
            stale.Finish();
            await staging.MapAsync();
            staging.CopyFrom([new(2, node)]);
            staging.Unmap();
            RequireThrows(
                () =>
                {
                    using IGraphicsSubmission unexpected = device.Queue.Submit([stale]);
                },
                "Submit accepted staging bytes rewritten after copy recording.");
        }

        // An unsubmitted copy must not publish metadata or trigger a draw-time transfer.
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        IComputeEncoder compute = commands.BeginComputePass(new());
        compute.SetPipeline(pipeline);
        compute.SetArgumentTable(table);
        var arguments = new BindingComputeArguments(Matrix4x4.Identity, 0, node, result);
        compute.SetArguments(in arguments);
        RequireThrows(() => compute.Dispatch(1), "Dispatch accepted shader data without an explicit submitted or preceding copy.");
        compute.End();
        commands.Finish();
    }

    private static void RequireThrows(Action action, string message)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        throw new InvalidOperationException(message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
