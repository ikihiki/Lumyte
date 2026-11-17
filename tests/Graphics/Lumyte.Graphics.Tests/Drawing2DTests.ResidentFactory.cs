using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises the actual eleven-slot retention factory.</summary>
public sealed partial class Drawing2DTests
{
    private static Renderer2D CreateResidentRenderer(IGraphicDevice device)
    {
        var resources = new ExternalDrawingResources(device, builtinIdentity: true);
        try
        {
            using RendererBuildHost2D host = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: RetentionGpuLimits(maxFramesInFlight: 3, maxCachedAssets: 512, maxCachedBytes: 64 * 1024 * 1024, maxWorkingBytes: 256 * 1024 * 1024))[
                Renderer.FillRect()[StandardFillRect()],
                Renderer.FillRoundedRect()[StandardFillRoundedRect()],
                Renderer.FillCircle()[StandardFillCircle()],
                Renderer.DrawLine()[StandardDrawLine()],
                Renderer.FillPath()[StandardFillPath()],
                Renderer.StrokePath()[StandardStrokePath()],
                Renderer.DrawImage()[StandardDrawImage()],
                Renderer.DrawCoverageMask()[StandardDrawCoverageMask()],
                Renderer.DrawDistanceField()[StandardDrawDistanceField()],
                Renderer.DrawMesh()[StandardDrawMesh()],
                Renderer.DrawPaintGraph()[StandardDrawPaintGraph()]].CreateHost();
            return (Renderer2D)host.BuildRenderer(device, resources.Resources, resources, null);
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }
}
