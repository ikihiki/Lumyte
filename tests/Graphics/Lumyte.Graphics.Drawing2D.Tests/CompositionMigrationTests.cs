using System.Numerics;
using Lumyte.Composition;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Mathematics.Colors;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Verifies the generated public construction API across an assembly boundary.</summary>
public sealed partial class CompositionMigrationTests
{
    /// <summary>Checks every public drawing command reaches its dedicated interface in painter order.</summary>
    /// <param name="choices">Whether each passive route lowers a separate kind-local choice processor.</param>
    /// <returns>The asynchronous test completion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EveryComposedKindUsesOriginalInterfaceInPainterOrderAsync(bool choices)
    {
        var strategies = new DispatchRenderers();
        var device = new ProfilingDevice();
        using IRenderer2D renderer = Define(strategies.CreateTable(), choices).BuildRenderer(device, Resources(device), null, null);
        using var target = new DispatchTexture();
        using IGraphicsTextureView targetView = target.CreateView();
        using var imageTexture = new DispatchTexture();
        using IGraphicsTextureView imageView = imageTexture.CreateView();
        using var coverageTexture = new DispatchTexture { Format = TextureFormat.R8Unorm };
        using IGraphicsTextureView coverageView = coverageTexture.CreateView();
        using var sampler = new DispatchSampler();
        var canvas = new Canvas2D();
        Path2D path = Triangle();
        var rect = new Rect2D(0, 0, 8, 8);
        var color = new SRgba<float>(1, 0, 0, 0.5f);
        var paint = PaintGraph2D.Solid(color);
        var style = new StrokeStyle2D();
        var mesh = new Mesh2D(
            [new MeshVertex2D(Vector2.Zero, 1), new MeshVertex2D(new Vector2(8, 0), 1), new MeshVertex2D(new Vector2(0, 8), 1)],
            [0, 1, 2]);
        canvas.Begin();
        canvas.FillRect(rect, color);
        canvas.FillRect(rect with { X = 1 }, paint);
        canvas.FillRoundedRect(rect, 2, paint);
        canvas.FillCircle(new Vector2(4), 4, paint);
        canvas.DrawLine(Vector2.Zero, new Vector2(8), paint, style);
        canvas.FillPath(path, paint);
        canvas.StrokePath(path, paint, style);
        canvas.DrawImage(new Image2D(imageView, sampler), rect, rect);
        canvas.DrawCoverageMask(new CoverageImage2D(coverageView, sampler), rect, rect, paint);
        canvas.DrawDistanceField(new DistanceField2D(coverageView, sampler, DistanceFieldEncoding2D.Sdf, 4), rect, rect, paint);
        canvas.DrawMesh(mesh, paint);
        canvas.DrawPaintGraph(paint);
        canvas.FillRect(rect with { X = 2 }, color);

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new RenderTarget2D { View = targetView });
        Type[] expected =
        [
            typeof(FillRectCommand2D), typeof(FillRectCommand2D), typeof(FillRoundedRectCommand2D),
            typeof(FillCircleCommand2D), typeof(DrawLineCommand2D), typeof(FillPathCommand2D),
            typeof(StrokePathCommand2D), typeof(DrawImageCommand2D), typeof(DrawCoverageMaskCommand2D),
            typeof(DrawDistanceFieldCommand2D), typeof(DrawMeshCommand2D), typeof(DrawPaintGraphCommand2D),
            typeof(FillRectCommand2D),
        ];
        Assert.Equal(expected, strategies.Commands.Select(command => command.GetType()));
        Assert.Equal(12, strategies.Batches.Count);
        Assert.All(strategies.Batches, batch => Assert.Equal(0, batch.DisposeCount));
        using var commands = new DispatchCommands();
        prepared.Record(commands);
        Assert.Equal(
            expected.Where((type, index) => index == 0 || type != expected[index - 1]).Select(type => type.Name + ".draw"),
            strategies.Recorded);
    }

    /// <summary>Checks graph-local sharing creates one actual backend used by different typed routes.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task SharedBackendIsUsedAcrossKindsAndOwnedUntilLastPreparedUseAsync()
    {
        var device = new ProfilingDevice();
        var created = new List<SharedBackend>();
        var key = new SharedDeclaration<SharedBackend>("geometry", _ =>
        {
            var backend = new SharedBackend();
            created.Add(backend);
            return backend;
        });
        RendererBuildHost2D definition = DefineShared(key);
        DrawingResources2D resources = Resources(device);
        Assert.All(definition.DescribeDataflow(device, resources), route => Assert.Equal("geometry", route.SharedIdentity));
        Assert.Empty(created);
        IRenderer2D renderer = definition.BuildRenderer(device, resources, null, null);
        SharedBackend instance = Assert.Single(created);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        PreparedDraw2D prepared = await renderer.PrepareAsync(RectCircleRect(), new() { View = view });
        Assert.Equal([nameof(FillRectCommand2D), nameof(FillCircleCommand2D), nameof(FillRectCommand2D)], instance.Calls);
        Assert.All(instance.Plans, plan => Assert.Same(instance.Plans[0], plan));
        renderer.Dispose();
        Assert.Equal(0, instance.Disposals);
        prepared.Dispose();
        Assert.Equal(1, instance.Disposals);
        renderer.Dispose();
        using IRenderer2D second = definition.BuildRenderer(device, resources, null, null);
        Assert.Equal(2, created.Count);
        Assert.NotSame(created[0], created[1]);
    }

    /// <summary>Checks only the selected kind's pure admissions run and accepted preparation failures never fall through.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task FirstSupportedIsKindLocalAndDoesNotCatchBackendErrorsAsync()
    {
        var calls = new List<string>();
        var selected = new DispatchRenderers();
        var throwing = new DispatchRenderers { OnPreparing = _ => throw new InvalidOperationException("resource failure") };
        bool Admit(string name, bool supported)
        {
            calls.Add(name);
            return supported;
        }

        Definitions.Renderer declaration = StandardDefinition();
        var rect = new AdmissionRectangleRecipe((_, _) => Admit("unsupported", false), selected);
        var accepted = new AdmissionRectangleRecipe((_, _) => Admit("accepted", true), selected);
        var unreachable = new AdmissionRectangleRecipe((_, _) => throw new InvalidOperationException("unreachable candidate"), selected);
        _ = declaration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], Renderer.FillRect()[rect, accepted, unreachable]];
        var device = new ProfilingDevice();
        using RendererBuildHost2D definition = declaration.CreateHost();
        DrawingResources2D resources = Resources(device);
        Assert.Equal(3, definition.DescribeDataflow(device, resources)[0].ChoiceBackends.Count);
        Assert.Empty(calls);
        using IRenderer2D renderer = definition.BuildRenderer(device, resources, null, null);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 4, 4), new SRgba<float>(1, 0, 0, 1));
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        Assert.Equal(["unsupported", "accepted"], calls);
        Assert.Single(selected.Commands);
    }

    /// <summary>Checks missing and duplicate slots, invalid assignments, conflicting keys and device mismatch before any factory.</summary>
    [Fact]
    public void InvalidSlotsRejectBeforeAllocationAndFrozenDefinitionsRejectMutation()
    {
        var device = new ProfilingDevice();
        var backend = new DispatchRenderers();
        Definitions.Renderer empty = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: new DrawingRetentionOptions2D());
        Assert.Throws<ArgumentException>(() => empty.DescribeDataflow());
        var binding = new BorrowedPipeline2D<FillRectCommand2D>(backend);
        CompositionSlotAssignment<Definitions.Renderer> slot = Renderer.FillRect()[binding];
        Assert.Throws<ArgumentException>(() => _ = empty[slot, slot]);
        Assert.Throws<ArgumentException>(() => _ = empty[Renderer.FillCircle()[new IPipelineDeclaration2D<FillCircleCommand2D>[] { null! }]]);
        Assert.Throws<ArgumentException>(() => _ = empty[Renderer.FillRect()[binding, binding]]);
        Assert.Throws<InvalidOperationException>(() => _ = empty[default(CompositionSlotAssignment<Definitions.Renderer>)]);
        Assert.Throws<ArgumentNullException>(() => slot.Apply(null!));
        Assert.Throws<ArgumentNullException>(() => _ = Renderer.FillRect()[null!]);
        RendererBuildHost2D valid = Define(backend.CreateTable());
        DrawingResources2D validResources = Resources(device);
        Assert.Equal(11, valid.DescribeDataflow(device, validResources).Count);
        Assert.Throws<ArgumentException>(() => valid.DescribeDataflow(new ProfilingDevice(), validResources));
        Assert.Throws<InvalidOperationException>(() => valid.DescribeDataflow(device, Resources(device)));
        Assert.Throws<InvalidOperationException>(() => _ = valid.Declaration[Renderer.FillRect()[binding]]);
        Assert.Throws<InvalidOperationException>(() => _ = valid.Declaration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()]]);
        int creates = 0;
        SharedBackend Create()
        {
            creates++;
            return new();
        }

        var one = new SharedDeclaration<SharedBackend>("same", _ => Create());
        var two = new SharedDeclaration<SharedBackend>("same", _ => Create());
        RendererBuildHost2D conflict = DefineShared(one, two);
        Assert.Equal(11, conflict.DescribeDataflow(device, Resources(device)).Count);
        Assert.Equal(0, creates);
        Assert.Throws<ArgumentException>(() => valid.BuildRenderer(device, Resources(new ProfilingDevice()), null, null));
    }

    /// <summary>Checks captured arrays, explicit clearing and reusable assignments without parenting mutable nodes.</summary>
    [Fact]
    public void NamedSlotAssignmentsSnapshotAndReuseIndependentDefinitions()
    {
        var device = new ProfilingDevice();
        var backend = new DispatchRenderers();
        IPipelineDeclaration2D<FillRectCommand2D>[] children = [new BorrowedPipeline2D<FillRectCommand2D>(backend)];
        CompositionSlotAssignment<Definitions.Renderer> assignment = Renderer.FillRect()[children];
        children[0] = null!;
        RendererBuildHost2D first = Define(backend.CreateTable());
        RendererBuildHost2D second = Define(backend.CreateTable());
        _ = first.Declaration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()]];
        Assert.Throws<ArgumentException>(() => first.BuildRenderer(device, Resources(device), null, null));
        _ = first.Declaration[assignment];
        _ = second.Declaration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], assignment];
        Assert.Equal(11, first.DescribeDataflow(device, Resources(device)).Count);
        Assert.Equal(11, second.DescribeDataflow(device, Resources(device)).Count);
        first.Dispose();
        Assert.Throws<InvalidOperationException>(() => assignment.Apply(first.Declaration));
        Assert.Throws<InvalidOperationException>(() => assignment.Apply(second.Declaration));
        Definitions.Renderer partial = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: new DrawingRetentionOptions2D())[assignment];
        Assert.Throws<ArgumentException>(() => partial.CreateHost().BuildRenderer(device, Resources(device), null, null));
        _ = partial[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()]];
        assignment.Apply(partial);
        Assert.False(typeof(IDisposable).IsAssignableFrom(partial.GetType()));
    }

    /// <summary>Checks a captured shared binding creates one actual manager per build, never at slot application.</summary>
    [Fact]
    public void ReusedSharedSlotAssignmentResolvesOncePerIndependentRenderer()
    {
        var device = new ProfilingDevice();
        var instances = new List<SharedBackend>();
        var shared = new SharedDeclaration<SharedBackend>("reusable", _ =>
        {
            var instance = new SharedBackend();
            instances.Add(instance);
            return instance;
        });
        CompositionSlotAssignment<Definitions.Renderer> assignment = Renderer.FillRect()[shared];
        Definitions.Renderer missing = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: new DrawingRetentionOptions2D())[assignment];
        var owner = new CallbackOwner(() => throw new InvalidOperationException("A failed definition must not consume its owner."));
        Assert.Throws<ArgumentException>(() => missing.CreateHost().BuildRenderer(device, Resources(device), owner, null));
        Assert.Empty(instances);
        Assert.False(typeof(IDisposable).IsAssignableFrom(missing.GetType()));
        RendererBuildHost2D first = DefineShared(shared);
        RendererBuildHost2D second = DefineShared(shared);
        _ = first.Declaration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], assignment];
        _ = second.Declaration[Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()], assignment];
        DrawingResources2D resources = Resources(device);
        Assert.Equal(11, first.DescribeDataflow(device, resources).Count);
        Assert.Equal(11, second.DescribeDataflow(device, resources).Count);
        Assert.Empty(instances);
        using IRenderer2D one = first.BuildRenderer(device, resources, null, null);
        Assert.Single(instances);
        using IRenderer2D two = second.BuildRenderer(device, resources, null, null);
        Assert.Equal(2, instances.Count);
        Assert.NotSame(instances[0], instances[1]);
        first.Dispose();
        second.Dispose();
        Assert.All(instances, instance => Assert.Equal(0, instance.Disposals));
        one.Dispose();
        two.Dispose();
        Assert.All(instances, instance => Assert.Equal(1, instance.Disposals));
    }

    /// <summary>Checks generated slot signatures enforce the eleven exact backend types and hide old route nodes.</summary>
    [Fact]
    public void GeneratedNamedSlotPublicSurfaceIsTypedAndHasNoGeneralChildren()
    {
        Type[] commands = [typeof(FillRectCommand2D), typeof(FillRoundedRectCommand2D), typeof(FillCircleCommand2D), typeof(DrawLineCommand2D), typeof(FillPathCommand2D), typeof(StrokePathCommand2D), typeof(DrawImageCommand2D), typeof(DrawCoverageMaskCommand2D), typeof(DrawDistanceFieldCommand2D), typeof(DrawMeshCommand2D), typeof(DrawPaintGraphCommand2D)];
        Type factory = Renderer.GetType();
        foreach (DrawKind2D kind in Enum.GetValues<DrawKind2D>())
        {
            System.Reflection.MethodInfo slot = Assert.Single(
                typeof(DrawingCompose).Assembly.GetExportedTypes()
                .SelectMany(type => type.GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)),
                method => method.Name == kind.ToString() && method.ReturnType.IsGenericType
                    && method.ReturnType.GetGenericTypeDefinition() == typeof(CompositionSlot<,>));
            Assert.Equal(typeof(Definitions.Renderer), slot.ReturnType.GenericTypeArguments[0]);
            Assert.Equal(typeof(IPipelineDeclaration2D<>).MakeGenericType(commands[(int)kind]), slot.ReturnType.GenericTypeArguments[1]);
            Assert.Equal(factory, Assert.Single(slot.GetParameters()).ParameterType);
        }

        Assert.Null(typeof(Definitions.Renderer).GetProperty("Children"));
        Assert.Null(typeof(Definitions).GetNestedType("FillRect"));
        Assert.DoesNotContain(typeof(DrawingCompose).Assembly.GetExportedTypes(), type => type.Name is "CommandRoute2D" or "RouteContent2D");
        Assert.Equal(
            typeof(CompositionSlotAssignment<Definitions.Renderer>[]),
            Assert.Single(typeof(Definitions.Renderer).GetProperty("Item")!.GetIndexParameters()).ParameterType);
    }

    /// <summary>Checks original builtin instances remain unwrapped and combined mixed-kind GPU records match the control.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task BuiltinRecordCombiningMatchesLegacyAndCustomKindIsNotBypassedAsync()
    {
        var oldDevice = new ProfilingDevice();
        var newDevice = new ProfilingDevice();
        using var legacy = new Renderer2D(oldDevice, DrawingCommandRenderers2D.CreateDefault());
        using IRenderer2D composed = Define(DrawingCommandRenderers2D.CreateDefault()).BuildRenderer(newDevice, Resources(newDevice), null, null);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        DrawList2D list = RectCircleRect();
        using PreparedDraw2D oldPrepared = await legacy.PrepareAsync(list, new() { View = view });
        using PreparedDraw2D newPrepared = await composed.PrepareAsync(list, new() { View = view });
        Assert.Equal(oldDevice.Records, newDevice.Records);
        Assert.Equal(oldDevice.ShaderUploads, newDevice.ShaderUploads);
        Assert.Equal(oldDevice.NativeBuffers, newDevice.NativeBuffers);
        Assert.Equal(oldDevice.Writes, newDevice.Writes);
        using var oldCommands = new ProfilingCommands();
        using var newCommands = new ProfilingCommands();
        oldPrepared.Record(oldCommands);
        newPrepared.Record(newCommands);
        Assert.Equal(oldCommands.Events.OfType<ValueTuple<uint, uint>>(), newCommands.Events.OfType<ValueTuple<uint, uint>>());
        Assert.Single(newCommands.Events.OfType<ValueTuple<uint, uint>>());
        Assert.Equal(oldCommands.Events.OfType<string>(), newCommands.Events.OfType<string>());
        var custom = new DispatchRenderers();
        var defaults = DrawingCommandRenderers2D.CreateDefault();
        var mixedDevice = new ProfilingDevice();
        using IRenderer2D mixed = Define(new(custom, defaults.FillRoundedRect, defaults.FillCircle, defaults.DrawLine, defaults.FillPath, defaults.StrokePath, defaults.DrawImage, defaults.DrawCoverageMask, defaults.DrawDistanceField, defaults.DrawMesh, defaults.DrawPaintGraph)).BuildRenderer(mixedDevice, Resources(mixedDevice), null, null);
        using PreparedDraw2D mixedPrepared = await mixed.PrepareAsync(list, new() { View = view });
        Assert.Equal(2, custom.Commands.Count);
        Assert.All(custom.Commands, command => Assert.IsType<FillRectCommand2D>(command));
        Assert.NotEmpty(mixedDevice.Records);
    }

    /// <summary>Checks partial construction rollback retains failed cleanup and retries before creating a fresh scope.</summary>
    [Fact]
    public void PartialBuildFailureRollsBackAndRetriesOwnedCleanup()
    {
        var device = new ProfilingDevice();
        var created = new List<SharedBackend>();
        var one = new SharedDeclaration<SharedBackend>("one", _ =>
        {
            var item = new SharedBackend { FailDisposeOnce = created.Count == 0 };
            created.Add(item);
            return item;
        });
        int attempts = 0;
        var two = new SharedDeclaration<SharedBackend>("two", _ =>
        {
            attempts++;
            if (attempts == 1)
            {
                throw new InvalidOperationException("injected factory failure");
            }

            return new();
        });
        RendererBuildHost2D definition = DefineShared(one, two);
        DrawingResources2D resources = Resources(device);
        Assert.Throws<AggregateException>(() => definition.BuildRenderer(device, resources, null, null));
        Assert.Equal(1, Assert.Single(created).DisposalAttempts);
        Assert.Equal(0, created[0].Disposals);
        using IRenderer2D renderer = definition.BuildRenderer(device, resources, null, null);
        Assert.Equal(2, attempts);
        Assert.Equal(2, created.Count);
        Assert.Equal(1, created[0].Disposals);
        renderer.Dispose();
        renderer.Dispose();
        Assert.Equal(1, created[1].Disposals);
    }

    /// <summary>Checks distinct configurations of the same backend type remain separate and borrowed instances are not disposed.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task DifferentSharedKeysAreNotTypeDeduplicatedAndBorrowedBackendRemainsBorrowedAsync()
    {
        var device = new ProfilingDevice();
        var oneInstance = new SharedBackend();
        var twoInstance = new SharedBackend();
        var one = new SharedDeclaration<SharedBackend>("options-one", _ => oneInstance);
        var two = new SharedDeclaration<SharedBackend>("options-two", _ => twoInstance);
        using IRenderer2D renderer = DefineShared(one, two).BuildRenderer(device, Resources(device), null, null);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        using (PreparedDraw2D prepared = await renderer.PrepareAsync(RectCircleRect(), new() { View = view }))
        {
            Assert.Equal(2, oneInstance.Calls.Count);
            Assert.Equal([nameof(FillCircleCommand2D)], twoInstance.Calls);
        }

        renderer.Dispose();
        Assert.Equal(1, oneInstance.Disposals);
        Assert.Equal(1, twoInstance.Disposals);
        var borrowed = new SharedBackend();
        using IRenderer2D borrowedRenderer = Define(new(borrowed, borrowed, borrowed, borrowed, borrowed, borrowed, borrowed, borrowed, borrowed, borrowed, borrowed)).BuildRenderer(device, Resources(device), null, null);
        borrowedRenderer.Dispose();
        Assert.Equal(0, borrowed.Disposals);
    }

    /// <summary>Checks a Graph host borrows the actual compiled manager and coordinator independently of the source renderer.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task GraphBridgeKeepsCompiledBackendAliveAfterSourceRendererDisposalAsync()
    {
        var device = new ProfilingDevice();
        var instance = new SharedBackend();
        var key = new SharedDeclaration<SharedBackend>("graph-manager", _ => instance);
        IRenderer2D renderer = DefineShared(key, argumentTables: DrawingArgumentTableMode.DrawLocal).BuildRenderer(device, Resources(device), null, null);
        using var texture = new DispatchTexture();
        using var graph = new Lumyte.Graphics.RenderGraph.RenderGraph(device);
        RenderGraphTexture handle = graph.ImportTexture(texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite));
        var bindings = new DrawingGraphBindings2D(graph);
        bindings.BindTexture(texture, handle);
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 8, 8), new SRgba<float>(1, 0, 0, 1));
        canvas.FillCircle(new(4, 4), 3, new SRgba<float>(0, 1, 0, 1));
        var target = new RenderTargetBinding2D { Texture = handle, Description = new() { Width = texture.Width, Height = texture.Height, Format = texture.Format, Usage = texture.Usage } };
        RenderGraphDrawList2D list = canvas.End();
        Assert.Throws<NotSupportedException>(() => new RenderGraphRenderer2D(new ExternalRenderer(device), list, target, bindings));
        Assert.Empty(instance.Calls);
        using var graphRenderer = new RenderGraphRenderer2D(renderer, list, target, bindings);
        renderer.Dispose();
        Assert.Equal(0, instance.Disposals);
        using PreparedDrawingPasses2D prepared = await graph.Append2D(graphRenderer);
        Assert.Equal([nameof(FillRectCommand2D), nameof(FillCircleCommand2D)], instance.Calls);
        Assert.All(instance.Plans, plan => Assert.Equal(DrawingArgumentTableMode.DrawLocal, plan.ArgumentTableMode));
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new ProfilingCommands();
        compiled.Record(commands);
        commands.Dispose();
        compiled.Dispose();
        graphRenderer.Dispose();
        Assert.Equal(0, instance.Disposals);
        prepared.Dispose();
        Assert.Equal(1, instance.Disposals);
    }

    /// <summary>Checks the common interface retains immutable snapshots and resident no-op reuse without changing frame retirement.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ComposedRetainedSnapshotsReuseAndPreservePreviousVersionsAsync()
    {
        var device = new ProfilingDevice();
        using IRenderer2D renderer = Define(DrawingCommandRenderers2D.CreateDefault()).BuildRenderer(device, Resources(device), null, null);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView view = texture.CreateView();
        var target = new RenderTarget2D { View = view };
        DrawList2D original = RectCircleRect();
        using var snapshot = new DrawingSnapshot2D(1, [new(default, original, Matrix3x2.Identity, [], 1, 1)]);
        using (PreparedDrawingFrame2D first = await renderer.PrepareAsync(snapshot, target))
        {
            Complete(first);
        }

        long compiled = renderer.RetentionStatistics.CompiledRuns;
        using (PreparedDrawingFrame2D noop = await renderer.PrepareAsync(snapshot, target))
        {
            Complete(noop);
        }

        Assert.Equal(compiled, renderer.RetentionStatistics.CompiledRuns);
        Assert.Equal(1, renderer.RetentionStatistics.ReusedRuns);
        using var changed = new DrawingSnapshot2D(2, [new(default, RectCircleRect(), Matrix3x2.CreateTranslation(2, 0), [], 2, 2)]);
        using (PreparedDrawingFrame2D updated = await renderer.PrepareAsync(changed, target))
        {
            Complete(updated);
        }

        Assert.Equal(compiled + 1, renderer.RetentionStatistics.CompiledRuns);
        using PreparedDrawingFrame2D previous = await renderer.PrepareAsync(snapshot, target);
        Assert.Equal(3, original.Commands.Count);
        renderer.Dispose();
        Complete(previous);
    }

    /// <summary>Checks owned backend cleanup occurs before releasing its borrowed shader-resource dependencies.</summary>
    [Fact]
    public void SharedBackendIsDisposedBeforeExternalResourceOwner()
    {
        var device = new ProfilingDevice();
        var order = new List<string>();
        var shared = new SharedDeclaration<SharedBackend>("dependent", _ => new() { OnDispose = () => order.Add("backend") });
        var owner = new CallbackOwner(() => order.Add("resources"));
        using IRenderer2D renderer = DefineShared(shared).BuildRenderer(device, Resources(device), owner, null);
        renderer.Dispose();
        Assert.Equal(["backend", "resources"], order);
    }

    /// <summary>Checks the common Graph bridge does not construct implicit shaders for an existing entirely custom renderer.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task LegacyCustomGraphBorrowDoesNotAllocateBuiltinResourcesAsync()
    {
        var device = new DispatchDevice();
        var backend = new DispatchRenderers();
        using IRenderer2D renderer = new Renderer2D(device, backend.CreateTable());
        using var texture = new DispatchTexture();
        using var graph = new Lumyte.Graphics.RenderGraph.RenderGraph(device);
        RenderGraphTexture handle = graph.ImportTexture(texture, TextureState.ColorAttachment, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite));
        var bindings = new DrawingGraphBindings2D(graph);
        bindings.BindTexture(texture, handle);
        var canvas = new RenderGraphCanvas2D();
        canvas.Begin();
        canvas.FillRect(new(0, 0, 8, 8), new SRgba<float>(1, 0, 0, 1));
        var target = new RenderTargetBinding2D { Texture = handle, Description = new() { Width = texture.Width, Height = texture.Height, Format = texture.Format, Usage = texture.Usage } };
        using var graphRenderer = new RenderGraphRenderer2D(renderer, canvas.End(), target, bindings);
        renderer.Dispose();
        using PreparedDrawingPasses2D prepared = await graph.Append2D(graphRenderer);
        using CompiledRenderGraph compiled = graph.Compile();
        using var commands = new DispatchCommands();
        compiled.Record(commands);
        Assert.Equal(["FillRectCommand2D.draw"], backend.Recorded);
    }

    private static void Complete(PreparedDrawingFrame2D frame)
    {
        using var commands = new ProfilingCommands();
        frame.Record(commands);
        commands.Submit();
        using var submission = new CompletedSubmission();
        frame.AttachSubmission(submission);
        commands.Dispose();
        frame.Retire();
    }

    private static RendererBuildHost2D Define(DrawingCommandRenderers2D table, bool choices = false) => new(Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: new DrawingRetentionOptions2D { MaxFramesInFlight = 3, MaxCachedAssets = 32, MaxCachedBytes = 1024 * 1024 })[
        Renderer.FillRect()[choices ? new IPipelineDeclaration2D<FillRectCommand2D>[] { table.FillRect is DefaultFillRectRenderer2D ? StandardFillRect() : new BorrowedPipeline2D<FillRectCommand2D>(table.FillRect), new BorrowedPipeline2D<FillRectCommand2D>(new DispatchRenderers { OnPreparing = _ => throw new InvalidOperationException("unreachable") }) } : new IPipelineDeclaration2D<FillRectCommand2D>[] { table.FillRect is DefaultFillRectRenderer2D ? StandardFillRect() : new BorrowedPipeline2D<FillRectCommand2D>(table.FillRect) }],
        Renderer.FillRoundedRect()[choices ? new IPipelineDeclaration2D<FillRoundedRectCommand2D>[] { table.FillRoundedRect is DefaultFillRoundedRectRenderer2D ? StandardFillRoundedRect() : new BorrowedPipeline2D<FillRoundedRectCommand2D>(table.FillRoundedRect), new BorrowedPipeline2D<FillRoundedRectCommand2D>(new DispatchRenderers { OnPreparing = _ => throw new InvalidOperationException("unreachable") }) } : new IPipelineDeclaration2D<FillRoundedRectCommand2D>[] { table.FillRoundedRect is DefaultFillRoundedRectRenderer2D ? StandardFillRoundedRect() : new BorrowedPipeline2D<FillRoundedRectCommand2D>(table.FillRoundedRect) }],
        Renderer.FillCircle()[choices ? new IPipelineDeclaration2D<FillCircleCommand2D>[] { table.FillCircle is DefaultFillCircleRenderer2D ? StandardFillCircle() : new BorrowedPipeline2D<FillCircleCommand2D>(table.FillCircle), new BorrowedPipeline2D<FillCircleCommand2D>(new DispatchRenderers { OnPreparing = _ => throw new InvalidOperationException("unreachable") }) } : new IPipelineDeclaration2D<FillCircleCommand2D>[] { table.FillCircle is DefaultFillCircleRenderer2D ? StandardFillCircle() : new BorrowedPipeline2D<FillCircleCommand2D>(table.FillCircle) }],
        Renderer.DrawLine()[choices ? new IPipelineDeclaration2D<DrawLineCommand2D>[] { table.DrawLine is DefaultDrawLineRenderer2D ? StandardDrawLine() : new BorrowedPipeline2D<DrawLineCommand2D>(table.DrawLine), new BorrowedPipeline2D<DrawLineCommand2D>(new DispatchRenderers { OnPreparing = _ => throw new InvalidOperationException("unreachable") }) } : new IPipelineDeclaration2D<DrawLineCommand2D>[] { table.DrawLine is DefaultDrawLineRenderer2D ? StandardDrawLine() : new BorrowedPipeline2D<DrawLineCommand2D>(table.DrawLine) }],
        Renderer.FillPath()[choices ? new IPipelineDeclaration2D<FillPathCommand2D>[] { table.FillPath is DefaultFillPathRenderer2D ? StandardFillPath() : new BorrowedPipeline2D<FillPathCommand2D>(table.FillPath), new BorrowedPipeline2D<FillPathCommand2D>(new DispatchRenderers { OnPreparing = _ => throw new InvalidOperationException("unreachable") }) } : new IPipelineDeclaration2D<FillPathCommand2D>[] { table.FillPath is DefaultFillPathRenderer2D ? StandardFillPath() : new BorrowedPipeline2D<FillPathCommand2D>(table.FillPath) }],
        Renderer.StrokePath()[choices ? new IPipelineDeclaration2D<StrokePathCommand2D>[] { table.StrokePath is DefaultStrokePathRenderer2D ? StandardStrokePath() : new BorrowedPipeline2D<StrokePathCommand2D>(table.StrokePath), new BorrowedPipeline2D<StrokePathCommand2D>(new DispatchRenderers { OnPreparing = _ => throw new InvalidOperationException("unreachable") }) } : new IPipelineDeclaration2D<StrokePathCommand2D>[] { table.StrokePath is DefaultStrokePathRenderer2D ? StandardStrokePath() : new BorrowedPipeline2D<StrokePathCommand2D>(table.StrokePath) }],
        Renderer.DrawImage()[choices ? new IPipelineDeclaration2D<DrawImageCommand2D>[] { table.DrawImage is DefaultDrawImageRenderer2D ? StandardDrawImage() : new BorrowedPipeline2D<DrawImageCommand2D>(table.DrawImage), new BorrowedPipeline2D<DrawImageCommand2D>(new DispatchRenderers { OnPreparing = _ => throw new InvalidOperationException("unreachable") }) } : new IPipelineDeclaration2D<DrawImageCommand2D>[] { table.DrawImage is DefaultDrawImageRenderer2D ? StandardDrawImage() : new BorrowedPipeline2D<DrawImageCommand2D>(table.DrawImage) }],
        Renderer.DrawCoverageMask()[choices ? new IPipelineDeclaration2D<DrawCoverageMaskCommand2D>[] { table.DrawCoverageMask is DefaultDrawCoverageMaskRenderer2D ? StandardDrawCoverageMask() : new BorrowedPipeline2D<DrawCoverageMaskCommand2D>(table.DrawCoverageMask), new BorrowedPipeline2D<DrawCoverageMaskCommand2D>(new DispatchRenderers { OnPreparing = _ => throw new InvalidOperationException("unreachable") }) } : new IPipelineDeclaration2D<DrawCoverageMaskCommand2D>[] { table.DrawCoverageMask is DefaultDrawCoverageMaskRenderer2D ? StandardDrawCoverageMask() : new BorrowedPipeline2D<DrawCoverageMaskCommand2D>(table.DrawCoverageMask) }],
        Renderer.DrawDistanceField()[choices ? new IPipelineDeclaration2D<DrawDistanceFieldCommand2D>[] { table.DrawDistanceField is DefaultDrawDistanceFieldRenderer2D ? StandardDrawDistanceField() : new BorrowedPipeline2D<DrawDistanceFieldCommand2D>(table.DrawDistanceField), new BorrowedPipeline2D<DrawDistanceFieldCommand2D>(new DispatchRenderers { OnPreparing = _ => throw new InvalidOperationException("unreachable") }) } : new IPipelineDeclaration2D<DrawDistanceFieldCommand2D>[] { table.DrawDistanceField is DefaultDrawDistanceFieldRenderer2D ? StandardDrawDistanceField() : new BorrowedPipeline2D<DrawDistanceFieldCommand2D>(table.DrawDistanceField) }],
        Renderer.DrawMesh()[choices ? new IPipelineDeclaration2D<DrawMeshCommand2D>[] { table.DrawMesh is DefaultDrawMeshRenderer2D ? StandardDrawMesh() : new BorrowedPipeline2D<DrawMeshCommand2D>(table.DrawMesh), new BorrowedPipeline2D<DrawMeshCommand2D>(new DispatchRenderers { OnPreparing = _ => throw new InvalidOperationException("unreachable") }) } : new IPipelineDeclaration2D<DrawMeshCommand2D>[] { table.DrawMesh is DefaultDrawMeshRenderer2D ? StandardDrawMesh() : new BorrowedPipeline2D<DrawMeshCommand2D>(table.DrawMesh) }],
        Renderer.DrawPaintGraph()[choices ? new IPipelineDeclaration2D<DrawPaintGraphCommand2D>[] { table.DrawPaintGraph is DefaultDrawPaintGraphRenderer2D ? StandardDrawPaintGraph() : new BorrowedPipeline2D<DrawPaintGraphCommand2D>(table.DrawPaintGraph), new BorrowedPipeline2D<DrawPaintGraphCommand2D>(new DispatchRenderers { OnPreparing = _ => throw new InvalidOperationException("unreachable") }) } : new IPipelineDeclaration2D<DrawPaintGraphCommand2D>[] { table.DrawPaintGraph is DefaultDrawPaintGraphRenderer2D ? StandardDrawPaintGraph() : new BorrowedPipeline2D<DrawPaintGraphCommand2D>(table.DrawPaintGraph) }]]);

    private static RendererBuildHost2D DefineShared(SharedDeclaration<SharedBackend> key, SharedDeclaration<SharedBackend>? circle = null, DrawingArgumentTableMode argumentTables = DrawingArgumentTableMode.Auto) => new(Renderer(argumentTables: argumentTables, retention: new DrawingRetentionOptions2D())[
        Renderer.FillRect()[key],
        Renderer.FillRoundedRect()[key],
        Renderer.FillCircle()[circle ?? key],
        Renderer.DrawLine()[key],
        Renderer.FillPath()[key],
        Renderer.StrokePath()[key],
        Renderer.DrawImage()[key],
        Renderer.DrawCoverageMask()[key],
        Renderer.DrawDistanceField()[key],
        Renderer.DrawMesh()[key],
        Renderer.DrawPaintGraph()[key]]);

    private static DrawingResources2D Resources(IGraphicDevice device) => new(device, device.CreateGraphicsPipeline(new() { VertexShader = device.CreateShader(DrawingShaders2D.Vertex), FragmentShader = device.CreateShader(DrawingShaders2D.Fragment) }));

    private static DrawList2D RectCircleRect()
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        var paint = new SRgba<float>(1, 0, 0, 0.5f);
        canvas.FillRect(new(0, 0, 8, 8), paint);
        canvas.FillCircle(new Vector2(4), 3, paint);
        canvas.FillRect(new(1, 1, 4, 4), paint);
        return canvas.End();
    }

    private static Path2D Triangle()
    {
        var builder = new PathBuilder2D();
        builder.MoveTo(Vector2.Zero);
        builder.LineTo(new(8, 0));
        builder.LineTo(new(0, 8));
        builder.Close();
        return builder.Build();
    }

    private sealed class ExternalRenderer(IGraphicDevice device) : IRenderer2D
    {
        public IGraphicDevice Device => device;

        public DrawingRetentionStatistics2D RetentionStatistics => default;

        public ValueTask<PreparedDraw2D> PrepareAsync(DrawList2D list, RenderTarget2D target, CancellationToken cancellationToken = default) => throw new InvalidOperationException("External execution must not begin.");

        public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSceneReadLease2D source, RenderTarget2D target, CancellationToken cancellationToken = default) => throw new InvalidOperationException("External execution must not begin.");

        public ValueTask<PreparedDrawingFrame2D> PrepareAsync(DrawingSnapshot2D source, RenderTarget2D target, CancellationToken cancellationToken = default) => throw new InvalidOperationException("External execution must not begin.");

        public void Dispose()
        {
        }
    }

    private sealed class CallbackOwner(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private sealed class CompletedSubmission : IGraphicsSubmission
    {
        public SubmissionStatus Status => SubmissionStatus.Completed;

        public ValueTask WaitAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public void Dispose()
        {
        }
    }

    private sealed class SharedBackend : PipelineSharedService2D, IFillRectRenderer2D, IFillRoundedRectRenderer2D, IFillCircleRenderer2D, IDrawLineRenderer2D, IFillPathRenderer2D, IStrokePathRenderer2D, IDrawImageRenderer2D, IDrawCoverageMaskRenderer2D, IDrawDistanceFieldRenderer2D, IDrawMeshRenderer2D, IDrawPaintGraphRenderer2D, IDisposable
    {
        private readonly DispatchRenderers _backend = new();

        internal List<string> Calls { get; } = [];

        internal List<DrawingArgumentPlan2D> Plans { get; } = [];

        internal int Disposals { get; private set; }

        internal Action? OnDispose { get; init; }

        internal bool FailDisposeOnce { get; init; }

        internal int DisposalAttempts { get; private set; }

        public override void Dispose()
        {
            DisposalAttempts++;
            if (FailDisposeOnce && DisposalAttempts == 1)
            {
                throw new InvalidOperationException("injected cleanup failure");
            }

            OnDispose?.Invoke();
            Disposals++;
        }

        async ValueTask<IPreparedCommandBatch2D> IFillRectRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(FillRectCommand2D));
            Plans.Add(context.ArgumentPlan);
            IPreparedCommandBatch2D batch = await ((IFillRectRenderer2D)_backend).PrepareAsync(context, commands, cancellationToken);
            return new SharedUse(batch, context.RetainResources());
        }

        async ValueTask<IPreparedCommandBatch2D> IFillRoundedRectRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRoundedRectCommand2D> commands, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(FillRoundedRectCommand2D));
            Plans.Add(context.ArgumentPlan);
            IPreparedCommandBatch2D batch = await ((IFillRoundedRectRenderer2D)_backend).PrepareAsync(context, commands, cancellationToken);
            return new SharedUse(batch, context.RetainResources());
        }

        async ValueTask<IPreparedCommandBatch2D> IFillCircleRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillCircleCommand2D> commands, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(FillCircleCommand2D));
            Plans.Add(context.ArgumentPlan);
            IPreparedCommandBatch2D batch = await ((IFillCircleRenderer2D)_backend).PrepareAsync(context, commands, cancellationToken);
            return new SharedUse(batch, context.RetainResources());
        }

        async ValueTask<IPreparedCommandBatch2D> IDrawLineRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawLineCommand2D> commands, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(DrawLineCommand2D));
            Plans.Add(context.ArgumentPlan);
            IPreparedCommandBatch2D batch = await ((IDrawLineRenderer2D)_backend).PrepareAsync(context, commands, cancellationToken);
            return new SharedUse(batch, context.RetainResources());
        }

        async ValueTask<IPreparedCommandBatch2D> IFillPathRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillPathCommand2D> commands, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(FillPathCommand2D));
            Plans.Add(context.ArgumentPlan);
            IPreparedCommandBatch2D batch = await ((IFillPathRenderer2D)_backend).PrepareAsync(context, commands, cancellationToken);
            return new SharedUse(batch, context.RetainResources());
        }

        async ValueTask<IPreparedCommandBatch2D> IStrokePathRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<StrokePathCommand2D> commands, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(StrokePathCommand2D));
            Plans.Add(context.ArgumentPlan);
            IPreparedCommandBatch2D batch = await ((IStrokePathRenderer2D)_backend).PrepareAsync(context, commands, cancellationToken);
            return new SharedUse(batch, context.RetainResources());
        }

        async ValueTask<IPreparedCommandBatch2D> IDrawImageRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawImageCommand2D> commands, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(DrawImageCommand2D));
            Plans.Add(context.ArgumentPlan);
            IPreparedCommandBatch2D batch = await ((IDrawImageRenderer2D)_backend).PrepareAsync(context, commands, cancellationToken);
            return new SharedUse(batch, context.RetainResources());
        }

        async ValueTask<IPreparedCommandBatch2D> IDrawCoverageMaskRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawCoverageMaskCommand2D> commands, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(DrawCoverageMaskCommand2D));
            Plans.Add(context.ArgumentPlan);
            IPreparedCommandBatch2D batch = await ((IDrawCoverageMaskRenderer2D)_backend).PrepareAsync(context, commands, cancellationToken);
            return new SharedUse(batch, context.RetainResources());
        }

        async ValueTask<IPreparedCommandBatch2D> IDrawDistanceFieldRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawDistanceFieldCommand2D> commands, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(DrawDistanceFieldCommand2D));
            Plans.Add(context.ArgumentPlan);
            IPreparedCommandBatch2D batch = await ((IDrawDistanceFieldRenderer2D)_backend).PrepareAsync(context, commands, cancellationToken);
            return new SharedUse(batch, context.RetainResources());
        }

        async ValueTask<IPreparedCommandBatch2D> IDrawMeshRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawMeshCommand2D> commands, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(DrawMeshCommand2D));
            Plans.Add(context.ArgumentPlan);
            IPreparedCommandBatch2D batch = await ((IDrawMeshRenderer2D)_backend).PrepareAsync(context, commands, cancellationToken);
            return new SharedUse(batch, context.RetainResources());
        }

        async ValueTask<IPreparedCommandBatch2D> IDrawPaintGraphRenderer2D.PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<DrawPaintGraphCommand2D> commands, CancellationToken cancellationToken)
        {
            Calls.Add(nameof(DrawPaintGraphCommand2D));
            Plans.Add(context.ArgumentPlan);
            IPreparedCommandBatch2D batch = await ((IDrawPaintGraphRenderer2D)_backend).PrepareAsync(context, commands, cancellationToken);
            return new SharedUse(batch, context.RetainResources());
        }
    }

    private sealed class SharedUse(IPreparedCommandBatch2D batch, IDisposable use) : IPreparedCommandBatch2D
    {
        private bool _disposed;

        public void DeclareResources(IDrawingResourceDeclarations2D resources) => batch.DeclareResources(resources);

        public void Record(IGraphicsCommandBuffer commands, IGraphicsTextureView target) => batch.Record(commands, target);

        public void Dispose()
        {
            if (!_disposed)
            {
                batch.Dispose();
                use.Dispose();
                _disposed = true;
            }
        }
    }
}
