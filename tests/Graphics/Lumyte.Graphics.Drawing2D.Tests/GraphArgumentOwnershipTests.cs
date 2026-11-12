using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>CPU-only coverage of graph argument preparation, declarations, and compiled ownership.</summary>
public sealed class GraphArgumentOwnershipTests
{
    private static BarrierScope ShaderRead => new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);

    private static BarrierScope CopyWrite => new(PipelineStage.Copy, ResourceAccess.CopyWrite);

    /// <summary>Ownership transfers to the compiled graph and survives command completion and failed disposal.</summary>
    [Fact]
    public void CompiledGraphOwnsTablesUntilCommandsRetireAndCleanupSucceeds()
    {
        var device = new ProfilingDevice { FailTableDisposeOnce = true };
        using var graph = new Graph(device);
        using var sampler = new DispatchSampler();
        RenderGraphArguments arguments = graph.GetArguments();
        Assert.Same(arguments, graph.GetArguments());
        RenderGraphPass pass = graph.AddPass("draw", _ => { }).KeepAlive();
        ArgumentTableReservation scope = arguments.Reserve(pass);
        ArgumentRegistration<IGraphicsSampler> handle = scope.RegisterSampler(sampler);
        using CompiledRenderGraph compiled = graph.Compile();
        graph.Dispose();
        Assert.Empty(device.Tables);
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        Assert.Equal(1, device.ActiveTables);
        Assert.Same(scope.Table, ProfilingDevice.Owner(handle.Reference));
        commands.Finish();
        commands.Submit();
        Assert.Throws<InvalidOperationException>(compiled.Dispose);
        Assert.Equal(1, device.ActiveTables);
        commands.Complete();
        Assert.Throws<InvalidOperationException>(compiled.Dispose);
        Assert.Equal(1, device.ActiveTables);
        compiled.Dispose();
        compiled.Dispose();
        Assert.Equal(0, device.ActiveTables);
        Assert.Throws<InvalidOperationException>(() => handle.Reference);
    }

    /// <summary>Live foreign, undeclared, and incorrectly sampled resources fail before any GPU commands.</summary>
    /// <param name="invalidKind">The boundary test input.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DeclaredOnlyLookupRejectsInvalidLiveResourcesBeforeCommands(int invalidKind)
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using var foreign = new Graph(device);
        using var allocation = new DispatchTexture();
        RenderGraphTexture local = graph.ImportTexture(allocation, TextureState.Sampled, ShaderRead);
        using var foreignAllocation = new DispatchTexture();
        RenderGraphTexture other = foreign.ImportTexture(foreignAllocation, TextureState.Sampled, ShaderRead);
        bool recorded = false;
        RenderGraphPass pass = graph.AddPass("invalid", _ => recorded = true).KeepAlive();
        if (invalidKind == 2)
        {
            pass.UseTexture(local, TextureState.CopySource, new(PipelineStage.Copy, ResourceAccess.CopyRead));
        }

        RenderGraphArguments arguments = graph.GetArguments();
        ArgumentRegistration<IGraphicsTextureView> handle = arguments.RegisterTexture(arguments.Reserve(pass), invalidKind == 0 ? other : local);
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        Assert.Throws<ArgumentException>(() => compiled.Record(commands));
        Assert.False(recorded);
        Assert.Empty(commands.Events);
        Assert.Empty(device.Tables);
        Assert.Throws<InvalidOperationException>(() => handle.Reference);
        commands.Dispose();
    }

    /// <summary>Direct registrations cannot smuggle undeclared allocations around graph lookup validation.</summary>
    [Fact]
    public void DirectUndeclaredAllocationIsRejectedBeforeNativeTablesOrCommands()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        RenderGraphArguments arguments = graph.GetArguments();
        RenderGraphPass pass = graph.AddPass("direct", _ => throw new InvalidOperationException("Must not record")).KeepAlive();
        arguments.Reserve(pass).RegisterTexture(view);
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        Assert.Throws<ArgumentException>(() => compiled.Record(commands));
        Assert.Empty(commands.Events);
        Assert.Empty(device.Tables);
        commands.Dispose();
    }

    /// <summary>Culled reservations never invoke resolvers or allocate their transient resource or native table.</summary>
    [Fact]
    public void CulledPassNeverResolvesArgumentsOrAllocatesResources()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        RenderGraphBuffer<uint> transient = graph.CreateBuffer(new BufferDesc<uint> { Count = 4, Usage = BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        RenderGraphPass dead = graph.AddPass("dead", _ => throw new InvalidOperationException("Culled callback")).UseBuffer(transient, CopyWrite);
        RenderGraphArguments arguments = graph.GetArguments();
        ArgumentTableReservation scope = arguments.Reserve(dead);
        int resolved = 0;
        ArgumentRegistration<uint> handle = scope.RegisterBuffer<uint>(() =>
        {
            resolved++;
            throw new InvalidOperationException("Culled resolver");
        });
        bool finalized = false;
        arguments.OnMaterialized(scope, () => finalized = true);
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Empty(compiled.PassNames);
        Assert.Equal(new[] { "dead" }, compiled.CulledPassNames);
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        Assert.Equal(0, resolved);
        Assert.False(finalized);
        Assert.False(scope.IsMaterialized);
        Assert.Equal(0, device.NativeBuffers);
        Assert.Empty(device.Tables);
        Assert.Empty(commands.Events);
        Assert.Throws<InvalidOperationException>(() => handle.Reference);
        commands.Dispose();
    }

    /// <summary>Normalized graph-created views share, but equivalent caller views keep their distinct identities.</summary>
    [Fact]
    public void NormalizedGraphViewCacheDoesNotMergeCallerViews()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView callerA = texture.CreateView();
        using IGraphicsTextureView callerB = texture.CreateView();
        RenderGraphTexture resource = graph.ImportTexture(texture, TextureState.Sampled, ShaderRead);
        RenderGraphPass pass = graph.AddPass("views", _ => { }).UseTexture(resource, TextureState.Sampled, ShaderRead).KeepAlive();
        RenderGraphArguments arguments = graph.GetArguments();
        ArgumentTableReservation scope = arguments.Reserve(pass);
        ArgumentRegistration<IGraphicsTextureView> defaultView = arguments.RegisterTexture(scope, resource);
        ArgumentRegistration<IGraphicsTextureView> explicitView = arguments.RegisterTexture(scope, resource, new() { MipLevelCount = 1, ArrayLayerCount = 1 });
        ArgumentRegistration<IGraphicsTextureView> firstCaller = scope.RegisterTexture(callerA);
        ArgumentRegistration<IGraphicsTextureView> secondCaller = scope.RegisterTexture(callerB);
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        Assert.Equal(3u, Assert.Single(device.Tables).TextureCapacity);
        Assert.Same(defaultView.Reference, explicitView.Reference);
        Assert.NotSame(defaultView.Reference, firstCaller.Reference);
        Assert.NotSame(firstCaller.Reference, secondCaller.Reference);
        commands.Dispose();
    }

    /// <summary>Transient shader-data registration adds no implicit producer or hidden upload.</summary>
    [Fact]
    public void TransientShaderDataRequiresExplicitProducer()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        RenderGraphShaderDataBuffer<DrawingInstruction> data = graph.CreateShaderDataBuffer<DrawingInstruction>(DrawingShaders2D.Vertex, 1);
        RenderGraphPass read = graph.AddPass("read", _ => { }).UseShaderDataBuffer(data, ShaderRead).KeepAlive();
        RenderGraphArguments arguments = graph.GetArguments();
        arguments.RegisterBuffer(arguments.Reserve(read), data, 0, 1);
        Assert.Throws<InvalidOperationException>(() => graph.Compile());
        Assert.Equal(0, device.NativeBuffers);
        Assert.Empty(device.Tables);
        Assert.Equal(0, device.Copies);
    }

    /// <summary>An explicit transfer producer makes graph-owned shader-data references resolvable before recording.</summary>
    [Fact]
    public void TransientShaderDataMaterializesAfterExplicitCopyProducer()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using IGraphicsShaderDataBuffer<DrawingInstruction> upload = device.CreateShaderDataBuffer<DrawingInstruction>(DrawingShaders2D.Vertex, 1, MemoryPreference.Upload);
        RenderGraphShaderDataBuffer<DrawingInstruction> source = graph.ImportShaderDataBuffer(upload, new(PipelineStage.Host, ResourceAccess.HostWrite));
        RenderGraphShaderDataBuffer<DrawingInstruction> data = graph.CreateShaderDataBuffer<DrawingInstruction>(DrawingShaders2D.Vertex, 1);
        graph.AddPass("upload", context => context.Commands.CopyBuffer(context.GetShaderDataBuffer(source).SliceElements(0, 1), context.GetShaderDataBuffer(data).SliceElements(0, 1)))
            .UseShaderDataBuffer(source, new(PipelineStage.Copy, ResourceAccess.CopyRead)).UseShaderDataBuffer(data, CopyWrite);
        bool readyBeforeRead = false;
        RenderGraphPass read = graph.AddPass("read", _ => Assert.True(readyBeforeRead)).UseShaderDataBuffer(data, ShaderRead).KeepAlive();
        RenderGraphArguments arguments = graph.GetArguments();
        ArgumentTableReservation scope = arguments.Reserve(read);
        ArgumentRegistration<DrawingInstruction> handle = arguments.RegisterBuffer(scope, data, 0, 1);
        arguments.OnMaterialized(scope, () =>
        {
            Assert.Same(scope.Table, ProfilingDevice.Owner(handle.Reference));
            readyBeforeRead = true;
        });
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Equal(new[] { "upload", "read" }, compiled.PassNames);
        Assert.Equal(1, device.NativeBuffers);
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        Assert.Equal(2, device.NativeBuffers);
        Assert.Single(device.Tables);
        Assert.Contains("shader-copy", commands.Events);
        Assert.Equal(0, device.Copies);
        commands.Dispose();
    }

    /// <summary>Materialization uses live execution order, not reservation collection order.</summary>
    [Fact]
    public void MaterializationFollowsActualLivePassOrder()
    {
        var device = new ProfilingDevice { Caps = new() { ArgumentTableBindingLimits = new() { TextureSlots = 2 } } };
        using var graph = new Graph(device);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView a = texture.CreateView();
        using IGraphicsTextureView b = texture.CreateView();
        using IGraphicsTextureView c = texture.CreateView();
        RenderGraphTexture resource = graph.ImportTexture(texture, TextureState.Sampled, ShaderRead);
        RenderGraphPass first = graph.AddPass("first", _ => { }).UseTexture(resource, TextureState.Sampled, ShaderRead).KeepAlive();
        RenderGraphPass middle = graph.AddPass("middle", _ => { }).UseTexture(resource, TextureState.Sampled, ShaderRead).KeepAlive();
        RenderGraphPass last = graph.AddPass("last", _ => { }).UseTexture(resource, TextureState.Sampled, ShaderRead).KeepAlive();
        RenderGraphArguments arguments = graph.GetArguments();

        // Collection order last/first/middle would place last and first together. Execution must not.
        ArgumentTableReservation lastScope = arguments.Reserve(last);
        lastScope.RegisterTexture(c);
        ArgumentTableReservation firstScope = arguments.Reserve(first);
        firstScope.RegisterTexture(a);
        ArgumentTableReservation middleScope = arguments.Reserve(middle);
        middleScope.RegisterTexture(b);
        using CompiledRenderGraph compiled = graph.Compile();
        Assert.Equal(new[] { "first", "middle", "last" }, compiled.PassNames);
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        Assert.Equal(new uint[] { 2, 1 }, device.Tables.Select(desc => desc.TextureCapacity));
        Assert.Same(firstScope.Table, middleScope.Table);
        Assert.NotSame(firstScope.Table, lastScope.Table);
        commands.Dispose();
    }

    /// <summary>Ordinary drawing and real image-effect scopes share the graph allocator when limits permit.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task OrdinaryAndEffectPassesShareGraphOwnedTableAsync()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using var targetTexture = new DispatchTexture();
        RenderGraphTexture target = graph.ImportTexture(targetTexture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite));
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillCircle(new(8), 2, new Color2D(1, 0, 0, 1));
        canvas.BeginLayer(new() { ContentBounds = new(0, 0, 16, 16), Effects = scope => scope.AppendOpacity(scope.Source, 0.5f) });
        canvas.FillCircle(new(8), 3, new Color2D(0, 1, 0, 1));
        canvas.EndLayer();
        using var renderer = new RenderGraphRenderer2D(
            DrawingCommandRenderers2D.CreateDefault(),
            canvas.End(),
            new() { Texture = target, Description = new() { Width = 16, Height = 16, Format = TextureFormat.Rgba8Srgb, Usage = targetTexture.Usage }, LoadOp = AttachmentLoadOp.Clear },
            new DrawingGraphBindings2D(graph));
        using PreparedDrawingPasses2D prepared = await graph.Append2D(renderer);
        Assert.Empty(device.Tables);
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        Assert.Contains("image-effect", compiled.PassNames);
        Assert.Single(device.Tables);
        IArgumentTable table = Assert.Single(commands.Events.OfType<IArgumentTable>().Distinct());
        Assert.True(commands.Events.OfType<DrawingArguments>().Count() >= 2);
        Assert.All(commands.Events.OfType<DrawingArguments>(), root =>
        {
            Assert.Same(table, ProfilingDevice.Owner(root.Instructions));
            Assert.Same(table, ProfilingDevice.Owner(root.Data));
        });
        Assert.All(device.Instructions, instruction =>
        {
            Assert.Same(table, ProfilingDevice.Owner(instruction.Texture));
            Assert.Same(table, ProfilingDevice.Owner(instruction.Sampler));
        });
        commands.Dispose();
        compiled.Dispose();
        prepared.Dispose();
        Assert.Equal(0, device.ActiveTables);
    }

    /// <summary>Late builder mutations cannot widen the declaration snapshot of an already compiled plan.</summary>
    [Fact]
    public void PreparationUsesCompiledDeclarationsRatherThanMutableBuilderPass()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using var texture = new DispatchTexture();
        RenderGraphTexture image = graph.ImportTexture(texture, TextureState.Sampled, ShaderRead);
        RenderGraphPass pass = graph.AddPass("frozen", _ => { }).KeepAlive();
        using CompiledRenderGraph compiled = graph.Compile();
        pass.UseTexture(image, TextureState.Sampled, ShaderRead);
        compiled.Retain(new ProbePreparation(context => context.CreateTextureView(pass, image)));
        using var commands = new ProfilingCommands();
        Assert.Throws<ArgumentException>(() => compiled.Record(commands));
        Assert.Empty(commands.Events);
        commands.Dispose();
    }

    /// <summary>Opaque private buffers never exempt a registered graph allocation from its actual pass declaration.</summary>
    [Fact]
    public void PrivateBufferFlagCannotSmuggleAnUndeclaredGraphImport()
    {
        var device = new ProfilingDevice();
        using var graph = new Graph(device);
        using IGraphicsBuffer<uint> buffer = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.ShaderRead });
        graph.ImportBuffer(buffer, ShaderRead);
        RenderGraphArguments arguments = graph.GetArguments();
        ArgumentTableReservation reservation = arguments.Registrations.Reserve();
        reservation.RegisterBuffer(buffer.SliceElements(0, 1));
        RenderGraphPass pass = graph.AddPass("private", _ => { }).KeepAlive();
        arguments.Attach(pass, [reservation], privateBuffers: true);
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        Assert.Throws<ArgumentException>(() => compiled.Record(commands));
        Assert.Empty(device.Tables);
        Assert.Empty(commands.Events);
        commands.Dispose();
    }

    private sealed class ProbePreparation(Action<RenderGraphPreparationContext> prepare) : IRenderGraphPreparation
    {
        public void Validate()
        {
        }

        public void Prepare(RenderGraphPreparationContext context) => prepare(context);

        public void Dispose()
        {
        }
    }
}
