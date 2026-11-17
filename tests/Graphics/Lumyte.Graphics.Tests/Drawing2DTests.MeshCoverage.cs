using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.Vulkan;
using Lumyte.Mathematics.Colors;
using Xunit;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Tests;

/// <summary>Actual 2D Mesh dispatches compared against unchanged legacy full-pixel oracles.</summary>
public sealed partial class Drawing2DTests
{
    /// <summary>Checks antialiasing, ordered overlap, paint, clip, paths and DPR without weakening pixel equality.</summary>
    /// <returns>The native full-pixel comparison.</returns>
    [MeshGpuFact]
    public async Task VulkanMeshCoverageMatchesLegacyPixelsAsync()
    {
        using var device = VulkanDevice.Create();
        foreach (float dpr in new[] { 1f, 1.5f, 2f })
        {
            void Draw(RenderGraphCanvas2D canvas)
            {
                canvas.FillRect(new(0, 0, 32, 32), new SRgba<float>(0.1f, 0.2f, 0.3f, 1));
                canvas.FillRect(new(1.25f, 2.5f, 12.5f, 11.25f), new SRgba<float>(1, 0, 0, 0.5f));
                canvas.FillRoundedRect(new(5.25f, 3.5f, 12, 11), 3.25f, new SRgba<float>(0, 1, 0, 0.5f));
                canvas.FillCircle(new(10.5f, 11.25f), 6.25f, new SRgba<float>(0, 0, 1, 0.5f));
                canvas.ClipRect(new(2.25f, 1.5f, 18.5f, 19.25f));
                var path = new PathBuilder2D();
                path.MoveTo(new(1, 3));
                path.CubicTo(new(25, -2), new(-3, 25), new(19, 18));
                path.LineTo(new(2, 20));
                path.Close();
                Path2D shape = path.Build();
                var paint = PaintGraph2D.LinearGradient(new(0, 0), new(20, 15), [new(0, new(1, 0, 0, 0.5f)), new(1, new(0, 1, 1, 0.75f))], ExtendMode2D.Reflect);
                canvas.FillPath(shape, paint);
                canvas.StrokePath(shape, paint, new() { Width = 1.75f, Cap = LineCap2D.Round, Join = LineJoin2D.Round });
            }

            Vector4[] reference = await RenderAsync(device, 32, 32, Draw, pixelScale: dpr);
            PartitionCommands? recorded = null;
            Vector4[] actual = await RenderAsync(device, 32, 32, Draw, pixelScale: dpr, meshCoverage: true, observe: native => recorded = new(native));
            Assert.Equal(reference, actual);
            Assert.NotNull(recorded);
            Assert.True(recorded.MeshDispatches > 0);
            Assert.NotEmpty(recorded.Roots);
        }
    }

    /// <summary>Checks actual Mesh record table generations, translation roots and three live native versions.</summary>
    /// <returns>The full-pixel retained ownership proof.</returns>
    [MeshGpuFact]
    public async Task VulkanMeshRetainedVersionsAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckResidentVersionsAsync(device, meshCoverage: true);
        await CheckResidentColdSharingAsync(device, meshCoverage: true);
        await CheckResidentGraphAsync(device, meshCoverage: true);
    }

    /// <summary>Mesh batches retain the parent graph's opacity, clip and backdrop timing.</summary>
    /// <returns>The unchanged legacy full-pixel comparison.</returns>
    [MeshGpuFact]
    public async Task VulkanMeshParentGraphOpacityBackdropAsync()
    {
        using var device = VulkanDevice.Create();
        void Draw(RenderGraphCanvas2D canvas)
        {
            canvas.FillRect(new(0, 0, 16, 16), new SRgba<float>(0, 0, 1, 1));
            canvas.BeginLayer(new() { ContentBounds = new(2, 2, 10, 10), Effects = graph => graph.AppendOpacity(graph.Source, 0.5f) });
            canvas.FillRoundedRect(new(2.25f, 2.5f, 9, 9), 2, new SRgba<float>(1, 0, 0, 0.75f));
            canvas.FillCircle(new(8, 8), 5, new SRgba<float>(0, 1, 0, 0.5f));
            canvas.EndLayer();
            canvas.DrawBackdropEffect(new() { OutputBounds = new(5, 1, 4, 8), Effects = graph => graph.AppendEffect(new TranslateLayerEffect(2, 0), graph.Source) });
            canvas.FillRect(new(3, 3, 4, 5), new SRgba<float>(1, 1, 0, 0.25f));
        }

        Vector4[] expected = await RenderAsync(device, 16, 16, Draw);
        PartitionCommands? observed = null;
        Vector4[] actual = await RenderAsync(device, 16, 16, Draw, meshCoverage: true, observe: native => observed = new(native));
        Assert.Equal(expected, actual);
        Assert.NotNull(observed);
        Assert.True(observed.MeshDispatches > 0);
    }

    /// <summary>All eleven configured slots cover full paint, image/mask reference and indexed-legacy semantics.</summary>
    /// <returns>The existing unchanged strict full-pixel oracle suite.</returns>
    [MeshGpuFact]
    public async Task VulkanMeshAllSupportedKindsPaintReferencesAsync()
    {
        using var device = VulkanDevice.Create();
        await CheckFixedRecordsAsync(device, meshCoverage: true);
    }

    private static Renderer2D CreateMeshResidentRenderer(IGraphicDevice device)
    {
        var resources = new ExternalDrawingResources(device, builtinIdentity: true);
        try
        {
            using RendererBuildHost2D host = MeshCoverageConfiguration(resident: true).CreateHost();
            return (Renderer2D)host.BuildRenderer(device, resources.Resources, resources, null);
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }

    private static IRenderer2D CreateMeshCoverageRenderer(IGraphicDevice device, ExternalDrawingResources resources)
    {
        using RendererBuildHost2D host = MeshCoverageConfiguration().CreateHost();
        return host.BuildRenderer(device, resources.Resources, resources, null);
    }

    private static Definitions.Renderer MeshCoverageConfiguration(bool resident = false) => Renderer(
        argumentTables: DrawingArgumentTableMode.Auto,
        retention: resident ? RetentionGpuLimits(3, 32, 4 * 1024 * 1024, 16 * 1024 * 1024) : RetentionLimits(maxFramesInFlight: 3, maxCachedAssets: 32, maxCachedBytes: 4 * 1024 * 1024))[
        Renderer.FillRect()[MeshFillRect(options: MeshCoverageLimits(maxCommands: 1024, maxWorkingBytes: 256 * 1024 * 1024, numericPoolBytes: 8 * 1024 * 1024))],
        Renderer.FillRoundedRect()[MeshFillRoundedRect(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.FillCircle()[MeshFillCircle(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawLine()[MeshDrawLine(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.FillPath()[MeshFillPath(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.StrokePath()[MeshStrokePath(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawImage()[MeshDrawImage(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawCoverageMask()[MeshDrawCoverageMask(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawDistanceField()[MeshDrawDistanceField(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
        Renderer.DrawMesh()[StandardDrawMesh()],
        Renderer.DrawPaintGraph()[MeshDrawPaintGraph(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))]];
}
