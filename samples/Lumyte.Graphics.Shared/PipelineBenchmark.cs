using System.Diagnostics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Measures identical pipeline workloads using only common graphics APIs.</summary>
public static class PipelineBenchmark
{
    /// <summary>Measures first-use and repeated draw recording separately from submission completion.</summary>
    /// <param name="device">The backend device configured by the caller.</param>
    /// <param name="drawCount">The number of repeated draws.</param>
    /// <returns>CPU wall-clock timings in milliseconds.</returns>
    public static async Task<PipelineBenchmarkResult> RunAsync(IGraphicDevice device, int drawCount = 200)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(drawCount);
        using IGraphicsShader vertex = device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(PipelineBenchmark).Assembly, "Lumyte.Shaders.fullscreen-vertex.lshader"));
        using IGraphicsShader fragment = device.CreateShader(ShaderArtifact.LoadEmbedded(typeof(PipelineBenchmark).Assembly, "Lumyte.Shaders.color-fragment.lshader"));
        using IGraphicsPipeline pipeline = device.CreateGraphicsPipeline(new() { VertexShader = vertex, FragmentShader = fragment });
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new());
        commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
        IRenderEncoder render = commands.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
        render.SetPipeline(pipeline);
        render.SetRenderState(new() { ColorTargets = [new()] });
        render.SetViewport(new(0, 0, 8, 8, 0, 1));
        render.SetScissor(new(0, 0, 8, 8));
        render.SetBlendConstant(new(0, 0, 0, 0));
        render.SetStencilReference(0);
        long start = Stopwatch.GetTimestamp();
        render.Draw(3);
        double firstDraw = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        for (int i = 0; i < drawCount; i++)
        {
            render.Draw(3);
        }

        double repeatedDraws = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        render.End();
        commands.Finish();
        start = Stopwatch.GetTimestamp();
        using IGraphicsSubmission submission = device.Queue.Submit([commands]);
        await submission.WaitAsync();
        double completion = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        start = Stopwatch.GetTimestamp();
        pipeline.Dispose();
        double destruction = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return new(drawCount, firstDraw, repeatedDraws, completion, destruction);
    }
}
