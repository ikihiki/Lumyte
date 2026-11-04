using System.Diagnostics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text.Benchmarks;

internal sealed class TextFrame : IDisposable
{
    private PreparedDraw2D? _prepared;
    private IGraphicsCommandBuffer? _commands;
    private IGraphicsSubmission? _submission;
    private bool _completed;
    private long _submitted;

    private TextFrame()
    {
    }

    internal bool IsSubmitted => _submission is not null;

    internal double PrepareHostMs { get; private set; }

    internal double RecordHostMs { get; private set; }

    internal double EncodeHostMs { get; private set; }

    internal double SubmitHostMs { get; private set; }

    internal double CompletionObservationMs { get; private set; }

    public void Dispose()
    {
        if (_submission is not null && !_completed)
        {
            throw new InvalidOperationException("Drain the text frame before native teardown.");
        }

        _submission?.Dispose();
        _submission = null;
        _commands?.Dispose();
        _commands = null;
        _prepared?.Dispose();
        _prepared = null;
    }

    internal static async Task<TextFrame> CreateAsync(Renderer2D renderer, DrawList2D list, IGraphicsTextureView view)
    {
        var frame = new TextFrame();
        try
        {
            long start = Stopwatch.GetTimestamp();
            frame._prepared = await renderer.PrepareAsync(list, new RenderTarget2D { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = new Color2D(1, 1, 1, 1) });
            frame.PrepareHostMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            return frame;
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    internal void Submit(IGraphicDevice device, IGraphicsTexture texture, HardwareGpuTimer timer)
    {
        long start = Stopwatch.GetTimestamp();
        _commands = device.CreateCommandBuffer(new());
        if (timer.Supported)
        {
            timer.Begin(_commands);
        }

        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        _commands.Barrier(new TextureBarrierDesc
        {
            Texture = texture,
            Range = new(0, 1, 0, 1),
            BeforeState = TextureState.Undefined,
            AfterState = TextureState.ColorAttachment,
            Before = default,
            After = color,
        });
        long record = Stopwatch.GetTimestamp();
        _prepared!.Record(_commands);
        RecordHostMs = Stopwatch.GetElapsedTime(record).TotalMilliseconds;
        _commands.Barrier(new TextureBarrierDesc
        {
            Texture = texture,
            Range = new(0, 1, 0, 1),
            BeforeState = TextureState.ColorAttachment,
            AfterState = TextureState.CopySource,
            Before = color,
            After = new(PipelineStage.Copy, ResourceAccess.CopyRead),
        });
        if (timer.Supported)
        {
            timer.End(_commands);
        }

        _commands.Finish();
        EncodeHostMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        _submitted = Stopwatch.GetTimestamp();
        _submission = NativeCompletion.Submit(device, _commands);
        SubmitHostMs = Stopwatch.GetElapsedTime(_submitted).TotalMilliseconds;
    }

    internal async Task CompleteAsync()
    {
        if (_submission is null || _completed)
        {
            return;
        }

        await NativeCompletion.WaitAsync(_submission);
        CompletionObservationMs = Stopwatch.GetElapsedTime(_submitted).TotalMilliseconds;
        _completed = true;
    }
}
