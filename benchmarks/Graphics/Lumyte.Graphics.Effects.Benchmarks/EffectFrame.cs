using System.Diagnostics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Drawing2D.RenderGraph;
using Lumyte.Graphics.RenderGraph;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Effects.Benchmarks;

internal sealed class EffectFrame : IDisposable
{
    private readonly Graph _graph;
    private readonly RenderGraphRenderer2D _renderer;
    private PreparedDrawingPasses2D? _prepared;
    private CompiledRenderGraph? _plan;
    private IGraphicsCommandBuffer? _commands;
    private IGraphicsSubmission? _submission;
    private bool _completed;
    private long _submittedAt;

    private EffectFrame(IGraphicDevice device, IGraphicsTexture target, TextureDesc description, RenderGraphDrawList2D list)
    {
        _graph = new Graph(device, "D10-layer-effects");
        RenderGraphRenderer2D? renderer = null;
        try
        {
            RenderGraphTexture output = _graph.ImportTexture(target, TextureState.Undefined, default, "output");
            renderer = new RenderGraphRenderer2D(
                DrawingCommandRenderers2D.CreateDefault(),
                list,
                new RenderTargetBinding2D
                {
                    Texture = output,
                    Description = description,
                    LoadOp = AttachmentLoadOp.Clear,
                    ClearColor = new Color2D(0, 0, 1, 1),
                },
                new DrawingGraphBindings2D(_graph));
            _graph.ExportTexture(output, TextureState.CopySource, new(PipelineStage.Copy, ResourceAccess.CopyRead));
            _renderer = renderer;
        }
        catch
        {
            renderer?.Dispose();
            _graph.Dispose();
            throw;
        }
    }

    internal bool IsSubmitted => _submission is not null;

    internal double BuildPrepareMs { get; private set; }

    internal double CompileMs { get; private set; }

    internal double RecordMs { get; private set; }

    internal double EncodeMs { get; private set; }

    internal double SubmitMs { get; private set; }

    internal double CompletionLatencyMs { get; private set; }

    internal RenderGraphStatistics Statistics => _plan!.Statistics;

    internal RenderGraphSnapshot? Snapshot => _plan!.Snapshot;

    public void Dispose()
    {
        if (_submission is not null && !_completed)
        {
            throw new InvalidOperationException("Await confirmed GPU completion before disposing this frame.");
        }

        _submission?.Dispose();
        _submission = null;

        // Command disposal also discards a partial/unsubmitted recording after Record failure.
        _commands?.Dispose();
        _commands = null;
        _plan?.Dispose();
        _plan = null;
        _prepared?.Dispose();
        _prepared = null;
        _renderer.Dispose();
        _graph.Dispose();
    }

    internal static async Task<EffectFrame> CreateAsync(IGraphicDevice device, IGraphicsTexture target, TextureDesc description, RenderGraphDrawList2D list, bool capture)
    {
        long started = Stopwatch.GetTimestamp();
        var frame = new EffectFrame(device, target, description, list);
        try
        {
            frame._prepared = await frame._graph.Append2D(frame._renderer);
            frame.BuildPrepareMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            started = Stopwatch.GetTimestamp();
            frame._plan = frame._graph.Compile(capture: capture ? new RenderGraphCaptureOptions() : null);
            frame.CompileMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            return frame;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    internal void Submit(IGraphicDevice device, HardwareGpuTimer timer)
    {
        long started = Stopwatch.GetTimestamp();
        _commands = device.CreateCommandBuffer(new());
        if (timer.Supported)
        {
            timer.Begin(_commands);
        }

        long record = Stopwatch.GetTimestamp();
        _plan!.Record(_commands);
        RecordMs = Stopwatch.GetElapsedTime(record).TotalMilliseconds;
        if (timer.Supported)
        {
            timer.End(_commands);
        }

        _commands.Finish();
        EncodeMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _submittedAt = Stopwatch.GetTimestamp();
        try
        {
            _submission = device.Queue.Submit([_commands]);
        }
        catch (Exception error)
        {
            // Submission failure may be ambiguous. Never destroy/reuse potentially in-flight owners.
            Console.Error.WriteLine($"Submission outcome unknown; aborting process without native teardown: {error}");
            Environment.Exit(3);
            throw;
        }

        SubmitMs = Stopwatch.GetElapsedTime(_submittedAt).TotalMilliseconds;
    }

    internal async Task CompleteAsync()
    {
        if (_submission is null || _completed)
        {
            return;
        }

        try
        {
            await _submission.WaitAsync();
            if (_submission.Status != SubmissionStatus.Completed)
            {
                throw new InvalidOperationException("Submission did not confirm successful completion.");
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"GPU completion unconfirmed; aborting without native teardown: {error}");
            Environment.Exit(3);
            throw;
        }

        CompletionLatencyMs = Stopwatch.GetElapsedTime(_submittedAt).TotalMilliseconds;
        _completed = true;
    }
}
