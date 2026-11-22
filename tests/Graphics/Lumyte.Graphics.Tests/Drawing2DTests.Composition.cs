using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks compiled kind routes on hardware against the original table renderer.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks builtin coalescing and selected custom callback output on Vulkan hardware.</summary>
    /// <returns>The asynchronous pixel checks.</returns>
    [Trait("Execution", "Gpu")]
    [GpuFact]
    public async Task VulkanComposedKindsMatchLegacyAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckComposedKindsAsync(device);
    }

    /// <summary>Checks the same compiled graph and custom callback on the native WGSL backend.</summary>
    /// <returns>The asynchronous pixel checks.</returns>
    [Trait("Execution", "Gpu")]
    [GpuFact]
    public async Task WgpuComposedKindsMatchLegacyAsync()
    {
        using var device = WgpuDevice.Create();
        await CheckComposedKindsAsync(device);
    }

    private static async Task CheckComposedKindsAsync(IGraphicDevice device)
    {
        void Draw(RenderGraphCanvas2D canvas)
        {
            canvas.FillRect(new(0, 0, 16, 16), new SRgba<float>(1, 0, 0, 0.5f));
            canvas.FillCircle(new(8, 8), 6, new SRgba<float>(0, 1, 0, 0.5f));
            canvas.ClipRect(new(1.25f, 2.5f, 12, 11));
            canvas.FillPath(Rectangle(2, 3, 9, 8), new SRgba<float>(0, 0, 1, 0.5f));
            canvas.FillRect(new(4, 4, 9, 9), new SRgba<float>(1, 1, 0, 0.25f));
        }

        Vector4[] reference = await RenderAsync(device, 16, 16, Draw, useDefaultResources: true);
        Vector4[] composed = await RenderAsync(device, 16, 16, Draw, useDefaultResources: true, composed: true);
        Assert.Equal(reference, composed);
        var callback = new CountedCompositionRect();
        var admissions = new List<string>();
        bool SupportsFast(ReadOnlyMemory<FillRectCommand2D> commands)
        {
            admissions.Add("fast");
            foreach (FillRectCommand2D command in commands.Span)
            {
                if (command.Paint.Root is not SolidPaintNode2D || command.State.Transform != Matrix3x2.Identity || command.State.Clip.Elements.Count != 0)
                {
                    return false;
                }
            }

            return true;
        }

        IFillRectRenderer2D selected = new NativeAdmissionRectangle((_, commands) => SupportsFast(commands), callback);
        Vector4[] custom = await RenderAsync(device, 16, 16, Draw, useDefaultResources: true, composed: true, rectangleBackend: selected);
        Assert.Equal(reference, custom);
        Assert.Equal(2, callback.Calls);
        Assert.Equal(["fast", "fast"], admissions);
        AssertPixel(composed, 16, 0, 0, new(0.5f, 0, 0, 0.5f));
        var defaults = DrawingCommandRenderers2D.CreateDefault();
        using var directResources = new ExternalDrawingResources(device, builtinIdentity: true);
        using IRenderer2D direct = ComposeDefinition(defaults).BuildRenderer(device, directResources.Resources, directResources, null);
        await CheckStandaloneAsync(device, direct);
        Assert.Equal(1, directResources.Disposals);
        using var customResources = new ExternalDrawingResources(device, builtinIdentity: true);
        var customTable = new DrawingCommandRenderers2D(selected, defaults.FillRoundedRect, defaults.FillCircle, defaults.DrawLine, defaults.FillPath, defaults.StrokePath, defaults.DrawImage, defaults.DrawCoverageMask, defaults.DrawDistanceField, defaults.DrawMesh, defaults.DrawPaintGraph);
        using IRenderer2D customDirect = ComposeDefinition(customTable).BuildRenderer(device, customResources.Resources, customResources, null);
        await CheckStandaloneAsync(device, customDirect);
        Assert.Equal(1, customResources.Disposals);
        Assert.Equal(3, callback.Calls);
        int declaredCalls = 0;
        var recipe = new NativeRectangleRecipe(() => declaredCalls++);
        using RendererBuildHost2D declaredHost = ComposeDefinition(defaults);
        _ = declaredHost.Declaration[
            Renderer.FillRect()[Array.Empty<IPipelineDeclaration2D<FillRectCommand2D>>()],
            Renderer.FillRect()[recipe]];
        using var declaredResources = new ExternalDrawingResources(device, builtinIdentity: true);
        using IRenderer2D declaredRenderer = declaredHost.BuildRenderer(device, declaredResources.Resources, declaredResources, null);
        await CheckStandaloneAsync(device, declaredRenderer);
        Assert.Equal(1, declaredCalls);
        Assert.Equal(1, declaredResources.Disposals);
        using RendererBuildHost2D pathsHost = ComposeDefinition(defaults);
        _ = pathsHost.Declaration[
            Renderer.FillPath()[Array.Empty<IPipelineDeclaration2D<FillPathCommand2D>>()],
            Renderer.FillPath()[Lumyte.Resources.Graphics.AotSmoke.CustomCompose.Paths(maxSegments: 4096)],
            Renderer.StrokePath()[Array.Empty<IPipelineDeclaration2D<StrokePathCommand2D>>()],
            Renderer.StrokePath()[Lumyte.Resources.Graphics.AotSmoke.CustomCompose.Paths(maxSegments: 4096)]];
        using var pathsResources = new ExternalDrawingResources(device, builtinIdentity: true);
        using IRenderer2D pathsRenderer = pathsHost.BuildRenderer(device, pathsResources.Resources, pathsResources, null);
        await CheckSharedPathPixelsAsync(device, pathsRenderer);
    }

    private static async Task CheckSharedPathPixelsAsync(IGraphicDevice device, IRenderer2D renderer)
    {
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        Path2D path = Rectangle(2, 2, 10, 10);
        canvas.FillPath(path, new SRgba<float>(0, 0, 1, 1));
        canvas.StrokePath(path, new SRgba<float>(1, 0, 0, 1), new StrokeStyle2D { Width = 2 });
        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view, LoadOp = AttachmentLoadOp.Clear });
        renderer.Dispose();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite) });
        prepared.Record(commands);
        uint pitch = Math.Max(16 * 8, device.GetTextureCopyLayout(TextureFormat.Rgba16Float).BytesPerRowAlignment);
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * 16, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite), After = _copyRead });
        commands.CopyTextureToBuffer(new() { Texture = texture, Width = 16, Height = 16 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = 16 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = _copyWrite, After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        commands.Dispose();
        prepared.Dispose();

        // The shared manager is used by both real path processors through preparation and record.
        await readback.MapAsync();
        byte[] bytes = new byte[readback.Count];
        readback.CopyTo(bytes);
        readback.Unmap();
        var pixels = new Vector4[16 * 16];
        for (int y = 0; y < 16; y++)
        {
            for (int x = 0; x < 16; x++)
            {
                int offset = checked((int)((y * pitch) + (x * 8)));
                pixels[(y * 16) + x] = new(ReadHalf(bytes, offset), ReadHalf(bytes, offset + 2), ReadHalf(bytes, offset + 4), ReadHalf(bytes, offset + 6));
            }
        }

        AssertPixel(pixels, 16, 7, 7, new(0, 0, 1, 1));
        AssertPixel(pixels, 16, 2, 7, new(1, 0, 0, 1));
    }

    private static RendererBuildHost2D ComposeDefinition(DrawingCommandRenderers2D table) => new(Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: new DrawingRetentionOptions2D { MaxFramesInFlight = 3, MaxCachedAssets = 512, MaxCachedBytes = 64 * 1024 * 1024 })[
        Renderer.FillRect()[table.FillRect is DefaultFillRectRenderer2D ? new IPipelineDeclaration2D<FillRectCommand2D>[] { StandardFillRect() } : new IPipelineDeclaration2D<FillRectCommand2D>[] { new NativeAdmissionRecipe(table.FillRect), new BorrowedPipeline2D<FillRectCommand2D>(table.FillRect is NativeAdmissionRectangle admitted ? admitted.Backend : table.FillRect) }],
        Renderer.FillRoundedRect()[table.FillRoundedRect is DefaultFillRoundedRectRenderer2D ? StandardFillRoundedRect() : new BorrowedPipeline2D<FillRoundedRectCommand2D>(table.FillRoundedRect)],
        Renderer.FillCircle()[table.FillCircle is DefaultFillCircleRenderer2D ? StandardFillCircle() : new BorrowedPipeline2D<FillCircleCommand2D>(table.FillCircle)],
        Renderer.DrawLine()[table.DrawLine is DefaultDrawLineRenderer2D ? StandardDrawLine() : new BorrowedPipeline2D<DrawLineCommand2D>(table.DrawLine)],
        Renderer.FillPath()[table.FillPath is DefaultFillPathRenderer2D ? StandardFillPath() : new BorrowedPipeline2D<FillPathCommand2D>(table.FillPath)],
        Renderer.StrokePath()[table.StrokePath is DefaultStrokePathRenderer2D ? StandardStrokePath() : new BorrowedPipeline2D<StrokePathCommand2D>(table.StrokePath)],
        Renderer.DrawImage()[table.DrawImage is DefaultDrawImageRenderer2D ? StandardDrawImage() : new BorrowedPipeline2D<DrawImageCommand2D>(table.DrawImage)],
        Renderer.DrawCoverageMask()[table.DrawCoverageMask is DefaultDrawCoverageMaskRenderer2D ? StandardDrawCoverageMask() : new BorrowedPipeline2D<DrawCoverageMaskCommand2D>(table.DrawCoverageMask)],
        Renderer.DrawDistanceField()[table.DrawDistanceField is DefaultDrawDistanceFieldRenderer2D ? StandardDrawDistanceField() : new BorrowedPipeline2D<DrawDistanceFieldCommand2D>(table.DrawDistanceField)],
        Renderer.DrawMesh()[table.DrawMesh is DefaultDrawMeshRenderer2D ? StandardDrawMesh() : new BorrowedPipeline2D<DrawMeshCommand2D>(table.DrawMesh)],
        Renderer.DrawPaintGraph()[table.DrawPaintGraph is DefaultDrawPaintGraphRenderer2D ? StandardDrawPaintGraph() : new BorrowedPipeline2D<DrawPaintGraphCommand2D>(table.DrawPaintGraph)]]);

    private sealed class NativeRectangleRecipe(Action preparing) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(DeclaredRectangleProcessor);

        protected override object CreateElement(PipelineBuildContext2D context) => new DeclaredRectangleProcessor(preparing);
    }

    private sealed class DeclaredRectangleProcessor(Action preparing) : PipelineElement2D, IFillRectRenderer2D
    {
        private readonly IFillRectRenderer2D _backend = new DefaultFillRectRenderer2D();

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            preparing();
            return _backend.PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class CountedCompositionRect : IFillRectRenderer2D
    {
        private readonly IFillRectRenderer2D _backend = new DefaultFillRectRenderer2D();

        internal int Calls { get; private set; }

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default)
        {
            Calls++;
            return _backend.PrepareAsync(context, commands, cancellationToken);
        }
    }

    private sealed class NativeAdmissionRecipe(IFillRectRenderer2D backend) : PipelineDeclaration2D, IPipelineDeclaration2D<FillRectCommand2D>
    {
        public override Type ProcessorType => typeof(DeclaredAdmissionRectangle);

        protected override object CreateElement(PipelineBuildContext2D context) => new DeclaredAdmissionRectangle(backend);
    }

    private sealed class DeclaredAdmissionRectangle(IFillRectRenderer2D backend) : PipelineElement2D, IFillRectRenderer2D, IPipelineAdmission2D<FillRectCommand2D>
    {
        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands) => backend is not IPipelineAdmission2D<FillRectCommand2D> admission || admission.CanPrepare(context, commands);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default) => backend.PrepareAsync(context, commands, cancellationToken);
    }

    private sealed class NativeAdmissionRectangle(Func<RenderPreparationContext2D, ReadOnlyMemory<FillRectCommand2D>, bool> supports, IFillRectRenderer2D backend) : IFillRectRenderer2D, IPipelineAdmission2D<FillRectCommand2D>
    {
        internal IFillRectRenderer2D Backend => backend;

        public bool CanPrepare(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands) => supports(context, commands);

        public ValueTask<IPreparedCommandBatch2D> PrepareAsync(RenderPreparationContext2D context, ReadOnlyMemory<FillRectCommand2D> commands, CancellationToken cancellationToken = default) => backend.PrepareAsync(context, commands, cancellationToken);
    }
}
