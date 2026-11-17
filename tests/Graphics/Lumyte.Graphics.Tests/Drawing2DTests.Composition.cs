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
    [GpuFact]
    public async Task VulkanComposedKindsMatchLegacyAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckComposedKindsAsync(device);
    }

    /// <summary>Checks the same compiled graph and custom callback on the native WGSL backend.</summary>
    /// <returns>The asynchronous pixel checks.</returns>
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

        bool SupportsGeneral(ReadOnlyMemory<FillRectCommand2D> commands)
        {
            admissions.Add("general");
            return !commands.IsEmpty;
        }

        IFillRectRenderer2D selected = FirstSupported2D.FillRect(
            new((_, commands) => SupportsFast(commands), callback),
            new((_, commands) => SupportsGeneral(commands), callback));
        Vector4[] custom = await RenderAsync(device, 16, 16, Draw, useDefaultResources: true, composed: true, rectangleBackend: selected);
        Assert.Equal(reference, custom);
        Assert.Equal(2, callback.Calls);
        Assert.Equal(["fast", "fast", "general"], admissions);
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
            Renderer.FillRect()[FirstSupportedFillRect(candidates: [new((_, _) => true, recipe)])]];
        using var declaredResources = new ExternalDrawingResources(device, builtinIdentity: true);
        using IRenderer2D declaredRenderer = declaredHost.BuildRenderer(device, declaredResources.Resources, declaredResources, null);
        await CheckStandaloneAsync(device, declaredRenderer);
        Assert.Equal(1, declaredCalls);
        Assert.Equal(1, declaredResources.Disposals);
    }

    private static RendererBuildHost2D ComposeDefinition(DrawingCommandRenderers2D table) => new(Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: new DrawingRetentionOptions2D { MaxFramesInFlight = 3, MaxCachedAssets = 512, MaxCachedBytes = 64 * 1024 * 1024 })[
        Renderer.FillRect()[table.FillRect is DefaultFillRectRenderer2D ? StandardFillRect() : new BorrowedPipeline2D<FillRectCommand2D>(table.FillRect)],
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
}
