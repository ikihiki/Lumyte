using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Checks depth, stencil and indexed/indirect execution through common APIs.</summary>
public static class AdvancedCommandExercise
{
    /// <summary>Verifies exact pixels and GPU-generated dispatch output.</summary>
    /// <param name="device">The already created backend device.</param>
    /// <returns>The report after completion and explicit readback.</returns>
    public static async Task<string> RunAsync(IGraphicDevice device)
    {
        using IGraphicsShader vertex = Load(device, "advanced-vertex");
        using IGraphicsShader fragment = Load(device, "color-fragment");
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using IGraphicsPipeline depthOnly = device.CreateGraphicsPipeline(new() { VertexShader = vertex });
        await CheckDepthAsync(device, pipeline, depthOnly, TextureFormat.Depth32Float);
        await CheckDepthAsync(device, pipeline, depthOnly, TextureFormat.Depth24Stencil8);
        await CheckStencilAsync(device, pipeline);
        await CheckIndicesAsync(device, pipeline);
        await CheckIndirectAsync(device, pipeline);
        await CheckGpuDispatchAsync(device);
        CheckIndexLifetime(device);
        return "Advanced command checks passed: depth occlusion, depth-only load, stencil mask, 16/32-bit indexed ranges, signed baseVertex, indirect draw/indexed draw and GPU-generated dispatch.";
    }

    private static IGraphicsShader Load(IGraphicDevice device, string name) => device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(AdvancedCommandExercise).Assembly, "Lumyte.Shaders." + name + ".lshader"));

    private static GraphicsRenderStateDesc State(DepthStencilStateDesc? depth = null, ColorWriteMask mask = ColorWriteMask.All) => new()
    {
        ColorTargets = [new() { WriteMask = mask }],
        DepthStencil = depth ?? new(),
    };

    private static void Setup(IRenderEncoder render, IGraphicsPipeline pipeline, GraphicsRenderStateDesc state)
    {
        render.SetPipeline(pipeline);
        render.SetRenderState(state);
        render.SetViewport(new(0, 0, 8, 8, 0, 1));
        render.SetScissor(new(0, 0, 8, 8));
        render.SetBlendConstant(new(0, 0, 0, 0));
        render.SetStencilReference(0);
    }

    private static void Draw(IRenderEncoder render, Vector4 color, float depth)
    {
        var arguments = new AdvancedDrawArguments(color, depth);
        render.SetArguments(in arguments);
        render.Draw(3);
    }

    private static void Transition(IGraphicsCommandBuffer commands, IGraphicsTexture texture, TextureState before, TextureState after, BarrierScope source, BarrierScope destination)
        => commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = before, AfterState = after, Before = source, After = destination });

    private static IGraphicsTexture Color(IGraphicDevice device) => device.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });

    private static IGraphicsTexture Depth(IGraphicDevice device, TextureFormat format) => device.CreateTexture(new() { Width = 8, Height = 8, Format = format, Usage = TextureUsage.RenderAttachment });

    private static void ColorBarrier(IGraphicsCommandBuffer commands, IGraphicsTexture color) => Transition(commands, color, TextureState.Undefined, TextureState.ColorAttachment, default, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));

    private static void DepthBarrier(IGraphicsCommandBuffer commands, IGraphicsTexture depth) => Transition(commands, depth, TextureState.Undefined, TextureState.DepthStencilAttachment, default, new(PipelineStage.DepthStencil, ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite));

    private static RenderPassDesc Pass(IGraphicsTextureView color, IGraphicsTextureView? depth = null, AttachmentLoadOp depthLoad = AttachmentLoadOp.Clear) => new()
    {
        ColorAttachments = [new() { View = color, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(0, 0, 1, 1) }],
        DepthStencilAttachment = depth == null ? null : new() { View = depth, DepthLoadOp = depthLoad },
    };

    private static async Task CheckDepthAsync(IGraphicDevice device, IGraphicsPipeline pipeline, IGraphicsPipeline depthOnly, TextureFormat format)
    {
        using IGraphicsTexture color = Color(device);
        using IGraphicsTexture depth = Depth(device, format);
        using IGraphicsTextureView colorView = color.CreateView();
        using IGraphicsTextureView depthView = depth.CreateView();
        var depthState = new DepthStencilStateDesc { DepthTestEnable = true, DepthWriteEnable = true, DepthCompare = CompareFunction.Less };
        using (IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new()))
        {
            ColorBarrier(commands, color);
            DepthBarrier(commands, depth);
            IRenderEncoder render = commands.BeginRenderPass(Pass(colorView, depthView));
            Setup(render, pipeline, State(depthState));
            Draw(render, new(1, 0, 0, 1), 0.25f);
            Draw(render, new(0, 1, 0, 1), 0.75f);
            render.End();
            await ReadPixelsAsync(device, commands, color, _ => new(1, 0, 0, 1));
        }

        using (IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new()))
        {
            ColorBarrier(commands, color);
            DepthBarrier(commands, depth);
            IRenderEncoder render = commands.BeginRenderPass(new() { DepthStencilAttachment = new() { View = depthView } });
            Setup(render, depthOnly, new() { ColorTargets = [], DepthStencil = depthState });
            Draw(render, Vector4.One, 0.25f);
            render.End();
            var scope = new BarrierScope(PipelineStage.DepthStencil, ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite);
            Transition(commands, depth, TextureState.DepthStencilAttachment, TextureState.DepthStencilAttachment, scope, scope);
            render = commands.BeginRenderPass(Pass(colorView, depthView, AttachmentLoadOp.Load));
            Setup(render, pipeline, State(depthState));
            Draw(render, new(1, 0, 0, 1), 0.75f);
            render.End();
            await ReadPixelsAsync(device, commands, color, _ => new(0, 0, 1, 1));
        }

        using IGraphicsCommandBuffer invalid = device.CreateCommandBuffer(new());
        Expect<ArgumentException>(() => invalid.BeginRenderPass(new()));
        Expect<ArgumentException>(() => invalid.BeginRenderPass(new() { ColorAttachments = [new() { View = depthView, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] }));
        Expect<ArgumentException>(() => invalid.BeginRenderPass(new() { DepthStencilAttachment = new() { View = colorView } }));
        Expect<ArgumentException>(() => invalid.BeginRenderPass(new() { DepthStencilAttachment = new() { View = depthView, DepthClearValue = float.NaN } }));
        ColorBarrier(invalid, color);
        IRenderEncoder colorOnly = invalid.BeginRenderPass(Pass(colorView));
        Setup(colorOnly, pipeline, State(depthState));
        var root = new AdvancedDrawArguments(Vector4.One, 0.5f);
        colorOnly.SetArguments(in root);
        Expect<ArgumentException>(() => colorOnly.Draw(3));
        colorOnly.End();
        Expect<NotSupportedException>(() => device.GetTextureCopyLayout(TextureFormat.Depth32Float));
        Expect<NotSupportedException>(() => device.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Depth32Float, Usage = TextureUsage.Sampled }));
    }

    private static async Task CheckStencilAsync(IGraphicDevice device, IGraphicsPipeline pipeline)
    {
        using IGraphicsTexture color = Color(device);
        using IGraphicsTexture depth = Depth(device, TextureFormat.Depth24Stencil8);
        using IGraphicsTextureView colorView = color.CreateView();
        using IGraphicsTextureView depthView = depth.CreateView();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        ColorBarrier(commands, color);
        DepthBarrier(commands, depth);
        IRenderEncoder render = commands.BeginRenderPass(Pass(colorView, depthView));
        var replace = new StencilFaceDesc { Compare = CompareFunction.Always, Pass = StencilOperation.Replace };
        var stencil = new DepthStencilStateDesc { StencilTestEnable = true, Front = replace, Back = replace };
        Setup(render, pipeline, State(stencil, ColorWriteMask.None));
        render.SetStencilReference(7);
        render.SetScissor(new(0, 0, 4, 8));
        Draw(render, Vector4.One, 0.5f);
        var equal = new StencilFaceDesc { Compare = CompareFunction.Equal };
        render.SetRenderState(State(stencil with { Front = equal, Back = equal, StencilWriteMask = 0 }));
        render.SetScissor(new(0, 0, 8, 8));
        Draw(render, new(1, 0, 0, 1), 0.5f);
        render.End();
        await ReadPixelsAsync(device, commands, color, x => x < 4 ? new(1, 0, 0, 1) : new(0, 0, 1, 1));
    }

    private static async Task CheckIndicesAsync(IGraphicDevice device, IGraphicsPipeline pipeline)
    {
        // The selected range excludes sentinel indices; baseVertex turns 2,3,4 into 0,1,2.
        using IGraphicsBuffer<ushort> indices16 = device.CreateBuffer<ushort>(new() { Count = 6, Usage = BufferUsage.CopyDestination | BufferUsage.Index });
        using IGraphicsBuffer<uint> indices32 = device.CreateBuffer<uint>(new() { Count = 6, Usage = BufferUsage.CopyDestination | BufferUsage.Index });
        using IGraphicsBuffer<ushort> upload16 = await UploadAsync<ushort>(device, [999, 999, 2, 3, 4, 999]);
        using IGraphicsBuffer<uint> upload32 = await UploadAsync<uint>(device, [999, 999, 2, 3, 4, 999]);
        using IGraphicsTexture color = Color(device);
        using IGraphicsTextureView colorView = color.CreateView();
        for (int format = 0; format < 2; format++)
        {
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            if (format == 0)
            {
                commands.CopyBuffer(upload16.Slice(0, 6), indices16.Slice(0, 6));
                commands.Barrier(new BufferBarrierDesc<ushort> { Buffer = indices16.Slice(0, 6), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.IndexInput, ResourceAccess.IndexRead) });
            }
            else
            {
                commands.CopyBuffer(upload32.Slice(0, 6), indices32.Slice(0, 6));
                commands.Barrier(new BufferBarrierDesc<uint> { Buffer = indices32.Slice(0, 6), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.IndexInput, ResourceAccess.IndexRead) });
            }

            ColorBarrier(commands, color);
            IRenderEncoder render = commands.BeginRenderPass(Pass(colorView));
            Setup(render, pipeline, State());
            var root = new AdvancedDrawArguments(new(1, 0, 0, 1), 0.5f);
            render.SetArguments(in root);
            Expect<ArgumentException>(() => render.DrawIndexed(3));
            if (format == 0)
            {
                render.SetIndexBuffer(indices16.Slice(1, 4));
            }
            else
            {
                render.SetIndexBuffer(indices32.Slice(1, 4));
            }

            Expect<ArgumentException>(() => render.DrawIndexed(4, firstIndex: 1));
            render.DrawIndexed(0);
            render.DrawIndexed(3, firstIndex: 1, baseVertex: -2);
            IndexFormat indexFormat = format == 0 ? IndexFormat.Uint16 : IndexFormat.Uint32;
            IndexFormat wrongFormat = format == 0 ? IndexFormat.Uint32 : IndexFormat.Uint16;
            render.SetRenderState(State() with { Topology = PrimitiveTopology.TriangleStrip, StripIndexFormat = wrongFormat });
            Expect<ArgumentException>(() => render.DrawIndexed(3, firstIndex: 1, baseVertex: -2));
            render.SetRenderState(State() with { Topology = PrimitiveTopology.TriangleStrip, StripIndexFormat = indexFormat });
            Expect<ArgumentException>(() => render.Draw(3));
            render.DrawIndexed(3, firstIndex: 1, baseVertex: -2);
            render.End();
            await ReadPixelsAsync(device, commands, color, _ => new(1, 0, 0, 1));
        }
    }

    private static async Task CheckIndirectAsync(IGraphicDevice device, IGraphicsPipeline pipeline)
    {
        using IGraphicsBuffer<DrawIndirectArguments> draw = device.CreateBuffer<DrawIndirectArguments>(new() { Count = 2, Usage = BufferUsage.CopyDestination | BufferUsage.Indirect });
        using IGraphicsBuffer<DrawIndexedIndirectArguments> indexed = device.CreateBuffer<DrawIndexedIndirectArguments>(new() { Count = 2, Usage = BufferUsage.CopyDestination | BufferUsage.Indirect });
        using IGraphicsBuffer<uint> indices = device.CreateBuffer<uint>(new() { Count = 6, Usage = BufferUsage.CopyDestination | BufferUsage.Index });
        using IGraphicsBuffer<DrawIndirectArguments> drawUpload = await UploadAsync<DrawIndirectArguments>(device, [default, new() { VertexCount = 3, InstanceCount = 1 }]);
        using IGraphicsBuffer<DrawIndexedIndirectArguments> indexedUpload = await UploadAsync<DrawIndexedIndirectArguments>(device, [default, new() { IndexCount = 3, InstanceCount = 1, BaseVertex = -2 }]);
        using IGraphicsBuffer<uint> indexUpload = await UploadAsync<uint>(device, [999, 999, 2, 3, 4, 999]);
        using IGraphicsTexture color = Color(device);
        using IGraphicsTextureView view = color.CreateView();
        for (int mode = 0; mode < 3; mode++)
        {
            using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
            commands.CopyBuffer(drawUpload.Slice(0, 2), draw.Slice(0, 2));
            commands.CopyBuffer(indexedUpload.Slice(0, 2), indexed.Slice(0, 2));
            commands.CopyBuffer(indexUpload.Slice(0, 6), indices.Slice(0, 6));
            commands.Barrier(new BufferBarrierDesc<DrawIndirectArguments> { Buffer = draw.Slice(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.DrawIndirect, ResourceAccess.IndirectRead) });
            commands.Barrier(new BufferBarrierDesc<DrawIndexedIndirectArguments> { Buffer = indexed.Slice(0, 2), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.DrawIndirect, ResourceAccess.IndirectRead) });
            commands.Barrier(new BufferBarrierDesc<uint> { Buffer = indices.Slice(0, 6), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.IndexInput, ResourceAccess.IndexRead) });
            ColorBarrier(commands, color);
            IRenderEncoder render = commands.BeginRenderPass(Pass(view));
            Setup(render, pipeline, State());
            var root = new AdvancedDrawArguments(new(1, 0, 0, 1), 0.5f);
            render.SetArguments(in root);
            Expect<ArgumentException>(() => render.DrawIndirect(draw.Slice(0, 2)));
            Expect<InvalidOperationException>(() => render.DrawIndexedIndirect(indexed.Slice(1, 1)));
            if (mode == 0)
            {
                render.DrawIndirect(draw.Slice(1, 1));
            }
            else if (mode == 1)
            {
                render.SetIndexBuffer(indices.Slice(2, 3));
                render.DrawIndexedIndirect(indexed.Slice(1, 1));
            }
            else
            {
                render.DrawIndirect(draw.Slice(0, 1));
            }

            render.End();
            await ReadPixelsAsync(device, commands, color, _ => mode == 2 ? new(0, 0, 1, 1) : new(1, 0, 0, 1));
        }
    }

    private static async Task CheckGpuDispatchAsync(IGraphicDevice device)
    {
        using IGraphicsShader generate = Load(device, "indirect-generate");
        using IGraphicsShader execute = Load(device, "indirect-execute");
        using IGraphicsComputePipeline generator = device.CreateComputePipeline(new() { ComputeShader = generate });
        using IGraphicsComputePipeline consumer = device.CreateComputePipeline(new() { ComputeShader = execute });
        using IGraphicsBuffer<DispatchIndirectArguments> command = device.CreateBuffer<DispatchIndirectArguments>(new() { Count = 1, Usage = BufferUsage.ShaderWrite | BufferUsage.Indirect });
        using IGraphicsBuffer<uint> output = device.CreateBuffer<uint>(new() { Count = 3, Usage = BufferUsage.ShaderWrite | BufferUsage.CopySource });
        using IGraphicsBuffer<uint> readback = device.CreateBuffer<uint>(new() { Count = 3, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IArgumentTable table = device.CreateArgumentTable(new() { TextureCapacity = 1, SamplerCapacity = 1, BufferCapacity = 2 });
        var root = new IndirectComputeArguments(table.WriteBuffer(0, command.Slice(0, 1)), table.WriteBuffer(1, output.Slice(0, 3)));
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        IComputeEncoder compute = commands.BeginComputePass(new());
        compute.SetPipeline(generator);
        compute.SetArgumentTable(table);
        compute.SetArguments(in root);
        compute.Dispatch(1);
        compute.End();
        commands.Barrier(new BufferBarrierDesc<DispatchIndirectArguments> { Buffer = command.Slice(0, 1), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.DrawIndirect, ResourceAccess.IndirectRead) });
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = output.Slice(0, 3), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite) });
        compute = commands.BeginComputePass(new());
        compute.SetPipeline(consumer);
        compute.SetArgumentTable(table);
        var execution = new IndirectExecuteArguments(root.Output);
        compute.SetArguments(in execution);
        compute.DispatchIndirect(command.Slice(0, 1));
        compute.End();
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = output.Slice(0, 3), Before = new(PipelineStage.ComputeShader, ResourceAccess.ShaderWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyBuffer(output.Slice(0, 3), readback.Slice(0, 3));
        commands.Barrier(new BufferBarrierDesc<uint> { Buffer = readback.Slice(0, 3), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        uint[] values = new uint[3];
        readback.CopyTo(values);
        readback.Unmap();
        Require(values.SequenceEqual(new uint[] { 10, 20, 30 }), "GPU-generated indirect dispatch did not execute three workgroups.");
    }

    private static void CheckIndexLifetime(IGraphicDevice device)
    {
        using IGraphicsTexture color = Color(device);
        using IGraphicsTextureView view = color.CreateView();
        using IGraphicsBuffer<uint> indices = device.CreateBuffer<uint>(new() { Count = 3, Usage = BufferUsage.Index });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        ColorBarrier(commands, color);
        IRenderEncoder render = commands.BeginRenderPass(Pass(view));
        render.SetIndexBuffer(indices.Slice(0, 3));
        render.End();
        commands.Finish();
        indices.Dispose();
        Expect<ObjectDisposedException>(() => device.Queue.Submit([commands]));
    }

    private static async Task<IGraphicsBuffer<T>> UploadAsync<T>(IGraphicDevice device, T[] values)
        where T : unmanaged
    {
        IGraphicsBuffer<T> buffer = device.CreateBuffer<T>(new() { Count = (ulong)values.Length, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
        await buffer.MapAsync();
        buffer.CopyFrom(values);
        buffer.Unmap();
        return buffer;
    }

    private static async Task ReadPixelsAsync(IGraphicDevice device, IGraphicsCommandBuffer commands, IGraphicsTexture color, Func<uint, Vector4> expected)
    {
        uint pitch = Math.Max(32, device.GetTextureCopyLayout(color.Format).BytesPerRowAlignment);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 8, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        Transition(commands, color, TextureState.ColorAttachment, TextureState.CopySource, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), new(PipelineStage.Copy, ResourceAccess.CopyRead));
        commands.CopyTextureToBuffer(new() { Texture = color, Width = 8, Height = 8 }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 8 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] bytes = new byte[checked((int)readback.Count)];
        readback.CopyTo(bytes);
        readback.Unmap();
        for (uint y = 0; y < 8; y++)
        {
            for (uint x = 0; x < 8; x++)
            {
                Vector4 value = expected(x);
                uint offset = (y * pitch) + (x * 4);
                Require(bytes[offset] == (byte)(value.X * 255) && bytes[offset + 1] == (byte)(value.Y * 255) && bytes[offset + 2] == (byte)(value.Z * 255) && bytes[offset + 3] == (byte)(value.W * 255), "Depth/stencil or indexed/indirect execution changed a pixel.");
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Expect<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
