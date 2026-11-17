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
    }

    private static Definitions.Renderer ComposeDefinition(DrawingCommandRenderers2D table) => Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: new DrawingRetentionOptions2D { MaxFramesInFlight = 3, MaxCachedAssets = 512, MaxCachedBytes = 64 * 1024 * 1024 })[
        Renderer.FillRect()[BackendBinding2D<IFillRectRenderer2D>.Borrowed(table.FillRect)],
        Renderer.FillRoundedRect()[BackendBinding2D<IFillRoundedRectRenderer2D>.Borrowed(table.FillRoundedRect)],
        Renderer.FillCircle()[BackendBinding2D<IFillCircleRenderer2D>.Borrowed(table.FillCircle)],
        Renderer.DrawLine()[BackendBinding2D<IDrawLineRenderer2D>.Borrowed(table.DrawLine)],
        Renderer.FillPath()[BackendBinding2D<IFillPathRenderer2D>.Borrowed(table.FillPath)],
        Renderer.StrokePath()[BackendBinding2D<IStrokePathRenderer2D>.Borrowed(table.StrokePath)],
        Renderer.DrawImage()[BackendBinding2D<IDrawImageRenderer2D>.Borrowed(table.DrawImage)],
        Renderer.DrawCoverageMask()[BackendBinding2D<IDrawCoverageMaskRenderer2D>.Borrowed(table.DrawCoverageMask)],
        Renderer.DrawDistanceField()[BackendBinding2D<IDrawDistanceFieldRenderer2D>.Borrowed(table.DrawDistanceField)],
        Renderer.DrawMesh()[BackendBinding2D<IDrawMeshRenderer2D>.Borrowed(table.DrawMesh)],
        Renderer.DrawPaintGraph()[BackendBinding2D<IDrawPaintGraphRenderer2D>.Borrowed(table.DrawPaintGraph)]];

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
