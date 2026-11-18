using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Mathematics.Colors;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Verification;

internal static class MembershipMultipageExercise
{
    internal static async Task RunAsync(IGraphicDevice device, IRenderer2D renderer)
    {
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillPath(Box(0), new SRgba<float>(1, 0, 0, 0.5f));
        canvas.FillPath(Box(20), new SRgba<float>(0, 1, 0, 0.5f));
        using var graph = new Graph(device, "membership-64k-two-pages");
        var description = new TextureDesc { Width = 128, Height = 128, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled | TextureUsage.CopySource };
        Lumyte.Graphics.RenderGraph.RenderGraphTexture target = graph.CreateTexture(description, "target");
        using var adapter = new RenderGraphRenderer2D(renderer, canvas.End(), new() { Texture = target, Description = description, LoadOp = AttachmentLoadOp.Clear }, new(graph));
        using PreparedDrawingPasses2D owner = await graph.Append2D(adapter);
        EffectImage image = graph.AppendOpacity(new(target, new(0, 0, 128, 128), EffectColorSemantic.DisplayLinear), 0.5f);
        uint alignment = device.GetTextureCopyLayout(TextureFormat.Rgba16Float).BytesPerRowAlignment;
        uint pitch = ((1024 + alignment - 1) / alignment) * alignment;
        Lumyte.Graphics.RenderGraph.RenderGraphBuffer<byte> readback = graph.CreateBuffer<byte>(new() { Count = pitch * 128, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }, "readback");
        graph.AddPass("copy", context => context.Commands.CopyTextureToBuffer(new() { Texture = context.GetTexture(image.Texture), Width = 128, Height = 128 }, new() { Buffer = context.GetBuffer(readback).SliceElements(0, pitch * 128), BytesPerRow = pitch, RowsPerImage = 128 }))
            .UseTexture(image.Texture, TextureState.CopySource, new(PipelineStage.Copy, ResourceAccess.CopyRead)).UseBuffer(readback, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        graph.ExportBuffer(readback, new(PipelineStage.Host, ResourceAccess.HostRead));
        using Lumyte.Graphics.RenderGraph.CompiledRenderGraph compiled = graph.Compile();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        compiled.Record(commands);
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        IGraphicsBuffer<byte> pixels = compiled.GetBuffer(readback);
        await pixels.MapAsync();
        byte[] bytes = new byte[pixels.Count];
        pixels.CopyTo(bytes);
        pixels.Unmap();
        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                bool first = x < 100 && y < 100;
                bool second = x >= 20 && x < 120 && y >= 20 && y < 120;
                Vector4 expected = first ? new(0.5f, 0, 0, 0.5f) : Vector4.Zero;
                if (second)
                {
                    expected = new Vector4(0, 0.5f, 0, 0.5f) + (expected * 0.5f);
                }

                expected *= 0.5f;
                for (int channel = 0; channel < 4; channel++)
                {
                    int offset = checked((int)(y * pitch) + (x * 8) + (channel * 2));
                    float actual = (float)BitConverter.UInt16BitsToHalf(System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset, 2)));
                    if (actual != expected[channel])
                    {
                        throw new InvalidOperationException($"Membership multipage pixel ({x},{y}) channel {channel}: {actual} != {expected[channel]}.");
                    }
                }
            }
        }

        commands.Dispose();
        Console.WriteLine("CoverageMembership multipage64KiB Graph opacity all128x128 pixels passed.");
    }

    private static Path2D Box(float offset)
    {
        var path = new PathBuilder2D();
        path.MoveTo(new(offset, offset));
        path.LineTo(new(offset + 100, offset));
        path.LineTo(new(offset + 100, offset + 100));
        path.LineTo(new(offset, offset + 100));
        path.Close();
        return path.Build();
    }

    // Verification-only range adapter; every physical binding limit and native operation remains unchanged.
    internal sealed class RangeDevice(IGraphicDevice native) : IGraphicDevice
    {
        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost { add => native.DeviceLost += value; remove => native.DeviceLost -= value; }

        public bool ValidationEnabled => native.ValidationEnabled;

        public GraphicsDeviceStatus Status => native.Status;

        public GraphicsDeviceLoss? Loss => native.Loss;

        public DeviceCaps Caps { get; } = BoundedCaps(native);

        public IGraphicsQueue Queue => native.Queue;

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => native.WaitForRetirementAsync(cancellationToken);

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged => native.GetBufferLayout<T>();

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged => native.CreateBuffer(desc);

        public IGraphicsTexture CreateTexture(TextureDesc desc) => native.CreateTexture(desc);

        public IGraphicsSampler CreateSampler(SamplerDesc desc) => native.CreateSampler(desc);

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc) => native.CreateArgumentTable(desc);

        public IGraphicsShader CreateShader(ShaderArtifact artifact) => native.CreateShader(artifact);

        public IGraphicsSemaphore CreateSemaphore() => native.CreateSemaphore();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => native.CreateCommandBuffer(desc);

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => native.GetTextureCopyLayout(format);

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => native.CreateGraphicsPipeline(desc);

        public IMeshPipeline CreateMeshPipeline(MeshPipelineDesc desc) => native.CreateMeshPipeline(desc);

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => native.CreateComputePipeline(desc);

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : struct, IShaderArguments => native.CreateShaderDataBuffer<T>(artifact, count, memory);

        private static DeviceCaps BoundedCaps(IGraphicDevice device)
        {
            ulong maximum = device.Caps.MaxStorageBufferBindingSize == 0 ? device.Caps.MaxBufferSize : device.Caps.MaxStorageBufferBindingSize;
            if (maximum < 65536 || device.Caps.ArgumentTableBindingLimits.MaximumBufferRangeBytes < 65536)
            {
                throw new NotSupportedException("The verification device must support an unchanged legal 64KiB range.");
            }

            return device.Caps with { MaxStorageBufferBindingSize = 65536, ArgumentTableBindingLimits = device.Caps.ArgumentTableBindingLimits with { MaximumBufferRangeBytes = 65536 } };
        }
    }
}
