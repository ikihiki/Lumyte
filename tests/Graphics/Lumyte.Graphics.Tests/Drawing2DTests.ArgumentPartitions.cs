using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks whole-draw argument partitions with real native resources and submissions.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks strict admitted texture budgets in direct and graph drawing on Vulkan.</summary>
    /// <returns>The asynchronous GPU verification.</returns>
    [GpuFact]
    public async Task VulkanArgumentPartitionsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckNativeArgumentPartitionsAsync(device);
    }

    /// <summary>Checks strict admitted texture budgets in direct and graph drawing on wgpu.</summary>
    /// <returns>The asynchronous GPU verification.</returns>
    [GpuFact]
    public async Task WgpuArgumentPartitionsAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckNativeArgumentPartitionsAsync(device);
    }

    /// <summary>Mis-sized WGSL storage ranges fail preflight before earlier valid reservations publish.</summary>
    /// <returns>The asynchronous GPU-device verification.</returns>
    [GpuFact]
    public async Task WgpuArgumentRangeSizePreflightAsync()
    {
        using var native = WgpuDevice.Create();
        var device = new PartitionDevice(native, native.Caps.ArgumentTableBindingLimits.TextureSlots);
        using IGraphicsBuffer<byte> buffer = device.CreateBuffer<byte>(new() { Count = 4, Usage = BufferUsage.ShaderRead });
        using var plan = new ArgumentTablePlan(device);
        IGpuRef<byte>? earlier = null;
        plan.Reserve().RegisterBuffer(buffer.SliceElements(0, 4), reference => earlier = reference);
        plan.Reserve().RegisterBuffer(buffer.SliceElements(0, 1), _ => throw new InvalidOperationException("An invalid range must never publish."));
        Assert.Throws<NotSupportedException>(() => plan.Materialize());
        Assert.Empty(device.Tables);
        Assert.Null(earlier);
        await Task.CompletedTask;
    }

    private static async Task CheckNativeArgumentPartitionsAsync(IGraphicDevice native)
    {
        // This is a stricter TEST admission budget, not a claimed physical hardware limit.

        // All other binding constraints, the actual native device and SourceCore limits stay intact.
        Assert.True(native.Caps.ArgumentTableBindingLimits.TextureSlots >= 4);
        var device = new PartitionDevice(native, 4);
        using IGraphicsTexture red = native.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        using IGraphicsTexture blue = native.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        await UploadAsync(native, red, [255, 0, 0, 255]);
        await UploadAsync(native, blue, [0, 0, 255, 255]);
        using IGraphicsSampler sampler = native.CreateSampler(new() { MinFilter = FilterMode.Nearest, MagFilter = FilterMode.Nearest, MipmapFilter = FilterMode.Nearest, AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge, LodMaxClamp = 0 });
        IGraphicsTextureView[] views = Enumerable.Range(0, 10).Select(index => (index % 2 == 0 ? red : blue).CreateView()).ToArray();
        try
        {
            Assert.Equal(10, views.Distinct(ReferenceEqualityComparer.Instance).Count());
            await CheckPartitionDirectAsync(device, views, sampler);
            device.Tables.Clear();
            await CheckPartitionGraphAsync(device, views, sampler);
        }
        finally
        {
            foreach (IGraphicsTextureView view in views)
            {
                view.Dispose();
            }
        }
    }

    private static async Task CheckPartitionDirectAsync(PartitionDevice device, IGraphicsTextureView[] views, IGraphicsSampler sampler)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int x = 0; x < views.Length; x++)
        {
            canvas.DrawImage(new(views[x], sampler), new(0, 0, 1, 1), new(x, 0, 1, 4));
        }

        using IGraphicsTexture target = device.CreateTexture(new() { Width = 10, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView targetView = target.CreateView();
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = targetView, LoadOp = AttachmentLoadOp.Clear });
        AssertPartitionDescriptions(device, graph: false);
        uint pitch = PartitionPitch(device);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer nativeCommands = device.CreateCommandBuffer(new());
        var commands = new PartitionCommands(nativeCommands);
        var attachment = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = attachment });
        prepared.Record(commands);
        commands.Barrier(new TextureBarrierDesc { Texture = target, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = attachment, After = _copyRead });
        PartitionCopy(commands, target, readback, pitch);
        commands.Finish();

        // Native queues accept the original native recording, never the observer wrapper.
        using IGraphicsSubmission submission = device.Queue.Submit([nativeCommands]);
        try
        {
            commands.AssertOwners(device.Tables, views);
            Assert.Equal(10, commands.Roots.Count);
            Assert.Equal(4, commands.Roots.Select(root => root.Table).Distinct().Count());
            for (int x = 0; x < 10; x++)
            {
                Assert.Same(device.Tables[x / 3].Table, commands.Roots[x].Table);
            }

            await submission.WaitAsync();
            await AssertPartitionPixelsAsync(readback, pitch, graph: false);
        }
        finally
        {
            // Also drain on an assertion failure before disposing commands/prepared resources.
            await submission.WaitAsync();
        }
    }

    private static async Task CheckPartitionGraphAsync(PartitionDevice device, IGraphicsTextureView[] views, IGraphicsSampler sampler)
    {
        using var graph = new Graph(device, "native-argument-partitions");
        var bindings = new DrawingGraphBindings2D(graph);
        foreach (IGraphicsTexture texture in views.Select(view => view.Texture).Distinct())
        {
            bindings.BindTexture(texture, graph.ImportTexture(texture, TextureState.Sampled, _sampleRead));
        }

        var description = new TextureDesc { Width = 10, Height = 4, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.Sampled | TextureUsage.CopySource };
        RenderGraphTexture target = graph.CreateTexture(description, "target");
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 10, 4), new SRgba<float>(0, 1, 0, 0.25f));
        canvas.BeginLayer(new() { ContentBounds = new(0, 0, 10, 4), Effects = effects => effects.AppendOpacity(effects.Source, 0.5f) });
        for (int x = 0; x < views.Length; x++)
        {
            canvas.DrawImage(new(views[x], sampler), new(0, 0, 1, 1), new(x, 0, 1, 4));
        }

        canvas.EndLayer();
        using var renderer = new RenderGraphRenderer2D(DrawingCommandRenderers2D.CreateDefault(), canvas.End(), new() { Texture = target, Description = description, LoadOp = AttachmentLoadOp.Clear }, bindings);
        using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
        uint pitch = PartitionPitch(device);
        RenderGraphBuffer<byte> result = graph.CreateBuffer<byte>(new() { Count = pitch * 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback }, "readback");
        graph.AddPass("readback", context => PartitionCopy(context.Commands, context.GetTexture(target), context.GetBuffer(result), pitch))
            .UseTexture(target, TextureState.CopySource, _copyRead).UseBuffer(result, _copyWrite);
        graph.ExportBuffer(result, new(PipelineStage.Host, ResourceAccess.HostRead));
        using CompiledRenderGraph plan = graph.Compile();
        Assert.Contains("image-effect", plan.PassNames);
        using IGraphicsCommandBuffer nativeCommands = device.CreateCommandBuffer(new());
        var commands = new PartitionCommands(nativeCommands);
        plan.Record(commands);
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([nativeCommands]);
        try
        {
            // Submit publishes shader-data transfer metadata; inspect before prepared/plan disposal.
            commands.AssertOwners(device.Tables, views);
            AssertPartitionDescriptions(device, graph: true);
            Assert.Equal(4, commands.Roots.Select(root => root.Table).Distinct().Count());

            // The final image, real effect and layer compositor fit the SAME last partition.
            Assert.Equal(13, commands.Roots.Count);
            Assert.Same(device.Tables[0].Table, commands.Roots[0].Table);
            for (int x = 0; x < 10; x++)
            {
                Assert.Same(device.Tables[x / 3].Table, commands.Roots[x + 1].Table);
            }

            Assert.Same(commands.Roots[^3].Table, commands.Roots[^2].Table);
            Assert.Same(commands.Roots[^2].Table, commands.Roots[^1].Table);
            await submission.WaitAsync();
            await AssertPartitionPixelsAsync(plan.GetBuffer(result), pitch, graph: true);
        }
        finally
        {
            await submission.WaitAsync();
        }
    }

    private static void AssertPartitionDescriptions(PartitionDevice device, bool graph)
    {
        // Fallback + three distinct caller views; the last direct partition has only one view.

        // In the graph, the layer source and effect output fill its two remaining slots.
        Assert.Equal(4, device.Tables.Count); // Native tables, NOT one count per resource kind.
        Assert.Equal(new uint[] { 4, 4, 4, graph ? 4u : 2u }, device.Tables.Select(item => item.Desc.TextureCapacity));
        Assert.Equal(new uint[] { 1, 1, 1, 1 }, device.Tables.Select(item => item.Desc.SamplerCapacity));

        // Two exact ranges per image; graph adds one rectangle range and two compositor ranges.
        Assert.Equal(graph ? new uint[] { 7, 6, 6, 4 } : new uint[] { 6, 6, 6, 2 }, device.Tables.Select(item => item.Desc.BufferCapacity));
    }

    private static uint PartitionPitch(IGraphicDevice device)
    {
        uint alignment = device.GetTextureCopyLayout(TextureFormat.Rgba16Float).BytesPerRowAlignment;
        return ((80 + alignment - 1) / alignment) * alignment;
    }

    private static void PartitionCopy(IGraphicsCommandBuffer commands, IGraphicsTexture target, IGraphicsBuffer<byte> readback, uint pitch)
    {
        commands.CopyTextureToBuffer(new() { Texture = target, Width = 10, Height = 4 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 4 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
    }

    private static async Task AssertPartitionPixelsAsync(IGraphicsBuffer<byte> readback, uint pitch, bool graph)
    {
        await readback.MapAsync();
        byte[] bytes = new byte[checked((int)readback.Count)];
        try
        {
            readback.CopyTo(bytes);
        }
        finally
        {
            readback.Unmap();
        }

        var pixels = new Vector4[40];
        for (int y = 0; y < 4; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                int offset = checked((int)((y * pitch) + (x * 8)));
                pixels[(y * 10) + x] = new(ReadHalf(bytes, offset), ReadHalf(bytes, offset + 2), ReadHalf(bytes, offset + 4), ReadHalf(bytes, offset + 6));

                // Premultiplied source-over: 0.5 opaque image + 0.5 * (0, .25, 0, .25).
                Vector4 expected = graph
                    ? (x % 2 == 0 ? new(0.5f, 0.125f, 0, 0.625f) : new Vector4(0, 0.125f, 0.5f, 0.625f))
                    : (x % 2 == 0 ? new(1, 0, 0, 1) : new Vector4(0, 0, 1, 1));
                AssertPixel(pixels, 10, x, y, expected);
            }
        }
    }

    // Only the admitted slot span is reduced. Every factory returns its REAL native object.
    private sealed class PartitionDevice : IGraphicDevice
    {
        private readonly IGraphicDevice _native;

        internal PartitionDevice(IGraphicDevice native, uint textureSlots)
        {
            _native = native;
            Assert.InRange(textureSlots, 1u, native.Caps.ArgumentTableBindingLimits.TextureSlots);
            Caps = native.Caps with { ArgumentTableBindingLimits = native.Caps.ArgumentTableBindingLimits with { TextureSlots = textureSlots } };
        }

        public event EventHandler<GraphicsDeviceLostEventArgs>? DeviceLost { add => _native.DeviceLost += value; remove => _native.DeviceLost -= value; }

        public GraphicsDeviceStatus Status => _native.Status;

        public GraphicsDeviceLoss? Loss => _native.Loss;

        public DeviceCaps Caps { get; }

        public IGraphicsQueue Queue => _native.Queue;

        internal List<(ArgumentTableDesc Desc, IArgumentTable Table)> Tables { get; } = [];

        public ValueTask WaitForRetirementAsync(CancellationToken cancellationToken = default) => _native.WaitForRetirementAsync(cancellationToken);

        public BufferLayout<T> GetBufferLayout<T>()
            where T : unmanaged => _native.GetBufferLayout<T>();

        public IGraphicsBuffer<T> CreateBuffer<T>(BufferDesc<T> desc)
            where T : unmanaged => _native.CreateBuffer(desc);

        public IGraphicsTexture CreateTexture(TextureDesc desc) => _native.CreateTexture(desc);

        public IGraphicsSampler CreateSampler(SamplerDesc desc) => _native.CreateSampler(desc);

        public IArgumentTable CreateArgumentTable(ArgumentTableDesc desc)
        {
            IArgumentTable table = _native.CreateArgumentTable(desc);
            Tables.Add((desc, table));
            return table;
        }

        public IGraphicsShader CreateShader(ShaderArtifact artifact) => _native.CreateShader(artifact);

        public IGraphicsSemaphore CreateSemaphore() => _native.CreateSemaphore();

        public IGraphicsCommandBuffer CreateCommandBuffer(CommandBufferDesc desc) => _native.CreateCommandBuffer(desc);

        public TextureCopyLayout GetTextureCopyLayout(TextureFormat format) => _native.GetTextureCopyLayout(format);

        public IGraphicsPipeline CreateGraphicsPipeline(GraphicsPipelineDesc desc) => _native.CreateGraphicsPipeline(desc);

        public IGraphicsComputePipeline CreateComputePipeline(ComputePipelineDesc desc) => _native.CreateComputePipeline(desc);

        public IGraphicsShaderDataBuffer<T> CreateShaderDataBuffer<T>(ShaderArtifact artifact, ulong count, MemoryPreference memory = MemoryPreference.Automatic)
            where T : IShaderArguments => _native.CreateShaderDataBuffer<T>(artifact, count, memory);
    }

    private sealed class PartitionCommands(IGraphicsCommandBuffer native) : IGraphicsCommandBuffer
    {
        public CommandBufferState State => native.State;

        internal List<(IArgumentTable Table, ShaderValueSnapshot Snapshot)> Roots { get; } = [];

        public void CopyBuffer<TSource, TDestination>(BufferSlice<TSource> source, BufferSlice<TDestination> destination)
            where TSource : unmanaged
            where TDestination : unmanaged => native.CopyBuffer(source, destination);

        public void CopyTexture(TextureCopyRegion source, TextureCopyRegion destination) => native.CopyTexture(source, destination);

        public void CopyBufferToTexture(BufferTextureCopyLayout source, TextureCopyRegion destination) => native.CopyBufferToTexture(source, destination);

        public void CopyTextureToBuffer(TextureCopyRegion source, BufferTextureCopyLayout destination) => native.CopyTextureToBuffer(source, destination);

        public void Barrier(MemoryBarrierDesc barrier) => native.Barrier(barrier);

        public void Barrier<T>(BufferBarrierDesc<T> barrier)
            where T : unmanaged => native.Barrier(barrier);

        public void Barrier(TextureBarrierDesc barrier) => native.Barrier(barrier);

        public IRenderEncoder BeginRenderPass(RenderPassDesc desc) => new PartitionRenderEncoder(native.BeginRenderPass(desc), Roots);

        public IComputeEncoder BeginComputePass(ComputePassDesc desc) => native.BeginComputePass(desc);

        public void Finish() => native.Finish();

        public void CopyBuffer<T>(ShaderDataSlice<T> source, ShaderDataSlice<T> destination)
            where T : IShaderArguments => native.CopyBuffer(source, destination);

        public void Barrier<T>(ShaderDataBufferBarrierDesc<T> barrier)
            where T : IShaderArguments => native.Barrier(barrier);

        public void Dispose() => native.Dispose();

        internal void AssertOwners(List<(ArgumentTableDesc Desc, IArgumentTable Table)> tables, IGraphicsTextureView[] views)
        {
            Assert.NotEmpty(Roots);
            int nested = 0;
            var sampled = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach ((IArgumentTable table, ShaderValueSnapshot snapshot) in Roots)
            {
                Assert.Contains(tables, item => ReferenceEquals(item.Table, table));
                var visited = new HashSet<(object Resource, ulong Offset, ulong Size)>();
                Visit(new ShaderDependencyMetadata(snapshot.Values.Select(value => Assert.IsAssignableFrom<IShaderReference>(value.Reference))).References, table, visited);
            }

            Assert.True(nested >= 10, "Every sampled image must be reached through shader-data metadata.");
            Assert.All(views, view => Assert.Contains(view, sampled));

            void Visit(IReadOnlyList<IShaderReference> values, IArgumentTable table, HashSet<(object Resource, ulong Offset, ulong Size)> visited)
            {
                foreach (IShaderReference reference in values)
                {
                    Assert.Same(table, reference.Table);
                    if (reference.Resource is IGraphicsTextureView view)
                    {
                        sampled.Add(view);
                    }

                    if (reference.Resource is not IShaderDataSource source || !visited.Add((reference.Resource, reference.OffsetInBytes, reference.SizeInBytes)))
                    {
                        continue;
                    }

                    ulong stride = checked((ulong)source.Layout.Size);
                    Assert.Equal(0ul, reference.OffsetInBytes % stride);
                    Assert.Equal(0ul, reference.SizeInBytes % stride);
                    for (ulong offset = 0; offset < reference.SizeInBytes; offset += stride)
                    {
                        IShaderReferenceMetadata child = source.ReadReferences((reference.OffsetInBytes + offset) / stride);
                        nested++;
                        Visit(child.References, table, visited);
                    }
                }
            }
        }
    }

    private sealed class PartitionRenderEncoder(IRenderEncoder native, List<(IArgumentTable Table, ShaderValueSnapshot Snapshot)> roots) : IRenderEncoder
    {
        private IArgumentTable? _table;
        private ShaderValueSnapshot? _arguments;

        public void SetArgumentValidation(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>?>? prepare) => native.SetArgumentValidation(prepare);

        public void SetArgumentTable(IArgumentTable table)
        {
            _table = table;
            native.SetArgumentTable(table);
        }

        public void SetArguments<T>(in T arguments)
            where T : IShaderArguments
        {
            ShaderValueSnapshot snapshot = IShaderArguments.Capture(in arguments);
            native.SetArguments(in arguments);
            _arguments = snapshot;
        }

        public void SetArguments<T>(in T arguments, string rootParameter = "arguments", string? shaderTypeName = null)
            where T : unmanaged
        {
            ShaderValueSnapshot snapshot = ShaderTransfer.Capture(in arguments, rootParameter, shaderTypeName);
            native.SetArguments(in arguments, rootParameter, shaderTypeName);
            _arguments = snapshot;
        }

        public void SetPipeline(IGraphicsPipeline pipeline) => native.SetPipeline(pipeline);

        public void SetRenderState(GraphicsRenderStateDesc state) => native.SetRenderState(state);

        public void SetViewport(Viewport viewport) => native.SetViewport(viewport);

        public void SetScissor(ScissorRect scissor) => native.SetScissor(scissor);

        public void SetBlendConstant(BlendConstant value) => native.SetBlendConstant(value);

        public void SetStencilReference(uint reference) => native.SetStencilReference(reference);

        public void Draw(uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        {
            CaptureDraw();
            native.Draw(vertexCount, instanceCount, firstVertex, firstInstance);
        }

        public void SetIndexBuffer(BufferSlice<ushort> indices) => native.SetIndexBuffer(indices);

        public void SetIndexBuffer(BufferSlice<uint> indices) => native.SetIndexBuffer(indices);

        public void DrawIndexed(uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
        {
            CaptureDraw();
            native.DrawIndexed(indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
        }

        public void DrawIndirect(BufferSlice<DrawIndirectArguments> arguments)
        {
            CaptureDraw();
            native.DrawIndirect(arguments);
        }

        public void DrawIndexedIndirect(BufferSlice<DrawIndexedIndirectArguments> arguments)
        {
            CaptureDraw();
            native.DrawIndexedIndirect(arguments);
        }

        public void End() => native.End();

        private void CaptureDraw()
        {
            // Native root and transitive shader-data capture happens per command, not when SetArguments selects numeric values.
            Assert.NotNull(_table);
            Assert.NotNull(_arguments);
            roots.Add((_table!, _arguments!.Copy()));
        }
    }
}
