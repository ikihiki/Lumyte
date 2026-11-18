using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.Pipelines;
using Lumyte.Graphics.Wgpu;
using Lumyte.Mathematics.Colors;
using static Lumyte.Graphics.Drawing2D.Pipelines.DrawingCompose;

namespace Lumyte.Resources.Graphics.AotSmoke;

internal static class MeshUnsupportedSmoke
{
    internal static async Task RunAsync()
    {
        using var device = WgpuDevice.Create();
        using IGraphicsShader vertex = device.CreateShader(DrawingShaders2D.Vertex);
        using IGraphicsShader fragment = device.CreateShader(DrawingShaders2D.Fragment);
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using RendererBuildHost2D host = Configuration().CreateHost();
        using IRenderer2D renderer = host.BuildRenderer(device, new(device, pipeline), null, null);
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 16, Height = 16, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView view = texture.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.FillRect(new(1, 1, 8, 8), new SRgba<float>(1, 0, 0, 1));
        try
        {
            using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        }
        catch (NotSupportedException error) when (error.Message == "Every candidate for the selected command kind rejected the batch.")
        {
            Console.WriteLine("Mesh unsupported: Wgpu rejected the Mesh-only candidate before shader/pipeline allocation or submission.");
            return;
        }

        throw new InvalidOperationException("Wgpu unexpectedly admitted a Mesh-only candidate.");
    }

    private static Definitions.Renderer Configuration(bool resident = false) => Renderer(
        argumentTables: DrawingArgumentTableMode.Auto,
        retention: resident ? RetentionGpuLimits(3, 32, 4 * 1024 * 1024, 16 * 1024 * 1024) : RetentionLimits(maxFramesInFlight: 3, maxCachedAssets: 32, maxCachedBytes: 4 * 1024 * 1024))[
        Renderer.FillRect()[MeshFillRect(options: MeshCoverageLimits(maxCommands: 32, maxWorkingBytes: 4 * 1024 * 1024, numericPoolBytes: 0))],
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
