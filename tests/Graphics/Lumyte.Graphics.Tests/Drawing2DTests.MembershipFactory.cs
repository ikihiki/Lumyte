using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises the actual eleven-slot coverage membership factory.</summary>
public sealed partial class Drawing2DTests
{
    private static Renderer2D CreateMembershipOracleRenderer(IGraphicDevice device, bool mixed = false)
    {
        var resources = new ExternalDrawingResources(device, builtinIdentity: true);
        try
        {
            IPipelineDeclaration2D<FillRectCommand2D> rectangles = mixed ? StandardFillRect() : CoverageMembershipFillRect();
            using RendererBuildHost2D host = Renderer(argumentTables: DrawingArgumentTableMode.Auto, retention: RetentionGpuLimits(maxFramesInFlight: 3, maxCachedAssets: 512, maxCachedBytes: 64 * 1024 * 1024, maxWorkingBytes: 256 * 1024 * 1024))[
                Renderer.FillRect()[rectangles],
                Renderer.FillRoundedRect()[CoverageMembershipFillRoundedRect()],
                Renderer.FillCircle()[CoverageMembershipFillCircle()],
                Renderer.DrawLine()[CoverageMembershipDrawLine()],
                Renderer.FillPath()[CoverageMembershipFillPath()],
                Renderer.StrokePath()[CoverageMembershipStrokePath()],
                Renderer.DrawImage()[CoverageMembershipDrawImage()],
                Renderer.DrawCoverageMask()[CoverageMembershipDrawCoverageMask()],
                Renderer.DrawDistanceField()[CoverageMembershipDrawDistanceField()],
                Renderer.DrawMesh()[CoverageMembershipDrawMesh()],
                Renderer.DrawPaintGraph()[CoverageMembershipDrawPaintGraph()]].CreateHost();
            return (Renderer2D)host.BuildRenderer(device, resources.Resources, resources, null);
        }
        catch
        {
            resources.Dispose();
            throw;
        }
    }
}
