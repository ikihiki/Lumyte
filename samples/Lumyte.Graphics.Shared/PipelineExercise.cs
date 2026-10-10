using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Exercises pipeline reuse and exact draw state solely through common APIs.</summary>
public static class PipelineExercise
{
    /// <summary>Draws with one program across formats and blend states, then dispatches compute.</summary>
    /// <param name="device">The already created backend device.</param>
    /// <returns>The report after GPU readback and lifecycle checks pass.</returns>
    public static async Task<string> RunAsync(IGraphicDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        using IGraphicsShader vertex = Load(device, "fullscreen-vertex");
        using IGraphicsShader fragment = Load(device, "color-fragment");
        using IGraphicsShader compute = Load(device, "empty-compute");
        Expect<ArgumentException>(() => device.CreateGraphicsPipeline(new() { VertexShader = fragment }));
        Expect<ArgumentException>(() => device.CreateComputePipeline(new() { ComputeShader = vertex }));
        using IGraphicsShader bound = Load(device, "increment");
        Expect<NotSupportedException>(() => device.CreateComputePipeline(new() { ComputeShader = bound }));
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        Expect<InvalidOperationException>(vertex.Dispose);
        Expect<InvalidOperationException>(fragment.Dispose);
        foreach (TextureFormat format in new[] { TextureFormat.Rgba8Unorm, TextureFormat.Bgra8Unorm })
        {
            // Repeating equivalent new state snapshots also exercises variant reuse.
            for (int repeat = 0; repeat < 2; repeat++)
            {
                await DrawAsync(device, pipeline, format, false, ColorWriteMask.All, uint.MaxValue);
                await DrawAsync(device, pipeline, format, true, ColorWriteMask.All, uint.MaxValue);
                await DrawAsync(device, pipeline, format, false, ColorWriteMask.None, uint.MaxValue);
                await DrawAsync(device, pipeline, format, false, ColorWriteMask.All, 0);
                await DrawAsync(device, pipeline, format, false, ColorWriteMask.All, uint.MaxValue, CullMode.Back);
                await DrawAsync(device, pipeline, format, false, ColorWriteMask.All, uint.MaxValue, CullMode.Front);
                await DrawAsync(device, pipeline, format, false, ColorWriteMask.All, uint.MaxValue, CullMode.None, 4);
            }
        }

        foreach (TextureFormat format in new[] { TextureFormat.R8Unorm, TextureFormat.Rg8Unorm, TextureFormat.R16Float, TextureFormat.Rg16Float, TextureFormat.Rgba16Float, TextureFormat.Rgb10A2Unorm })
        {
            await DrawAsync(device, pipeline, format, false, ColorWriteMask.All, uint.MaxValue);
            await DrawAsync(device, pipeline, format, false, ColorWriteMask.All, uint.MaxValue);
        }

        using IGraphicsComputePipeline computePipeline = device.CreateComputePipeline(new() { ComputeShader = compute });
        Expect<InvalidOperationException>(compute.Dispose);
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        IComputeEncoder encoder = commands.BeginComputePass(new());
        Expect<InvalidOperationException>(() => encoder.Dispatch(1));
        encoder.SetPipeline(computePipeline);
        Expect<ArgumentOutOfRangeException>(() => encoder.Dispatch(0));
        Expect<ArgumentOutOfRangeException>(() => encoder.Dispatch(uint.MaxValue));
        encoder.Dispatch(2, 3, 1);
        encoder.End();
        Expect<InvalidOperationException>(() => encoder.SetPipeline(computePipeline));
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        Require(commands.State == CommandBufferState.Completed, "Compute program did not complete.");
        pipeline.Dispose();
        pipeline.Dispose();
        computePipeline.Dispose();
        vertex.Dispose();
        fragment.Dispose();
        compute.Dispose();
        return "Pipeline checks passed: program reuse, RGBA/BGRA, blend, exact zero masks, immutable state, draw and compute dispatch.";
    }

    /// <summary>Rejects foreign-device modules before pipeline creation.</summary>
    /// <param name="device">The target device.</param>
    /// <param name="foreign">The independent device.</param>
    public static void CheckForeignDevice(IGraphicDevice device, IGraphicDevice foreign)
    {
        using IGraphicsShader shader = Load(foreign, "fullscreen-vertex");
        Expect<ArgumentException>(() => device.CreateGraphicsPipeline(new() { VertexShader = shader }));
    }

    private static IGraphicsShader Load(IGraphicDevice device, string name) => device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(PipelineExercise).Assembly, "Lumyte.Shaders." + name + ".lshader"));

    private static async Task DrawAsync(IGraphicDevice device, IGraphicsPipeline pipeline, TextureFormat format, bool blend, ColorWriteMask mask, uint sampleMask, CullMode cull = CullMode.None, uint scissorWidth = 8)
    {
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 8, Height = 8, Format = format, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = texture.CreateView();
        TextureCopyLayout layout = device.GetTextureCopyLayout(format);
        uint pitch = Math.Max(8 * layout.BytesPerTexel, layout.BytesPerRowAlignment);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 8, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        var write = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = write });
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store, ClearValue = new(0, 0, 1, 1) }] });
        Expect<InvalidOperationException>(() => render.Draw(3));
        render.SetPipeline(pipeline);
        var color = new ColorBlendStateDesc { BlendEnable = blend, WriteMask = mask, Color = new() { Source = BlendFactor.SourceAlpha, Destination = BlendFactor.OneMinusSourceAlpha } };
        var colors = new List<ColorBlendStateDesc> { color };
        var state = new GraphicsRenderStateDesc { ColorTargets = colors, SampleMask = sampleMask, Rasterization = new() { Cull = cull } };
        render.SetRenderState(state);
        colors[0] = color with { WriteMask = ColorWriteMask.None };
        render.SetViewport(new(0, 0, 8, 8, 0, 1));
        render.SetScissor(new(0, 0, scissorWidth, 8));
        render.SetBlendConstant(new(0, 0, 0, 0));
        render.SetStencilReference(0);
        Expect<ArgumentException>(() => render.SetViewport(new(0, 0, float.NaN, 8, 0, 1)));
        Expect<ArgumentException>(() => render.SetScissor(new(7, 0, 2, 8)));
        Expect<ArgumentException>(() => render.SetRenderState(state with { Topology = (PrimitiveTopology)99 }));
        render.SetPipeline(pipeline);
        render.Draw(0);
        render.Draw(3);
        render.End();
        Expect<InvalidOperationException>(() => render.Draw(3));
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = write, After = copy });
        commands.CopyTextureToBuffer(new() { Texture = texture, Width = 8, Height = 8 }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 8 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        await readback.MapAsync();
        byte[] bytes = new byte[checked((int)readback.Count)];
        readback.CopyTo(bytes);
        readback.Unmap();
        byte[]? exact = format switch
        {
            TextureFormat.R8Unorm => [255],
            TextureFormat.Rg8Unorm => [255, 0],
            TextureFormat.R16Float => [0, 60],
            TextureFormat.Rg16Float => [0, 60, 0, 0],
            TextureFormat.Rgba16Float => [0, 60, 0, 0, 0, 0, 0, 56],
            TextureFormat.Rgb10A2Unorm => [255, 3, 0, 128],
            _ => null,
        };

        for (uint y = 0; y < 8; y++)
        {
            for (uint x = 0; x < 8; x++)
            {
                if (exact != null)
                {
                    int index = checked((int)((y * pitch) + (x * layout.BytesPerTexel)));
                    Require(bytes.AsSpan(index, exact.Length).SequenceEqual(exact), "Extended format draw changed a pixel.");
                    continue;
                }

                bool skipped = mask == ColorWriteMask.None || sampleMask == 0 || cull == CullMode.Front || x >= scissorWidth;
                int red = skipped ? 0 : blend ? 128 : 255;
                int blue = skipped ? 255 : blend ? 128 : 0;
                int alpha = skipped ? 255 : 128;
                uint offset = (y * pitch) + (x * 4);
                int first = format == TextureFormat.Bgra8Unorm ? blue : red;
                int third = format == TextureFormat.Bgra8Unorm ? red : blue;
                Require(Math.Abs(bytes[offset] - first) <= 1 && bytes[offset + 1] == 0 && Math.Abs(bytes[offset + 2] - third) <= 1 && Math.Abs(bytes[offset + 3] - alpha) <= 1, "Pipeline draw, blend or exact zero mask changed a pixel.");
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
