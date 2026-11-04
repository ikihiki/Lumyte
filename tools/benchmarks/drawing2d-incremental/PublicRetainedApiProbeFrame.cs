using System.Diagnostics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Incremental.Benchmarks;

// Completion-safe frame/readback pattern from ProductionRetainedProbe.Slot.
internal sealed class PublicRetainedApiProbeFrame : IDisposable
{
    private readonly uint _size;
    private readonly uint _height;
    private readonly uint _pixelBytes;
    private long _submittedAt;
    private bool _initialized;
    private bool _recorded;
    private bool _submitAttempted;
    private bool _quarantined;

    internal PublicRetainedApiProbeFrame(VulkanDevice device, uint size, TextureFormat format, uint? height = null)
    {
        _size = size;
        _height = height ?? size;
        _pixelBytes = format == TextureFormat.Rgba16Float ? 8u : 4u;
        Texture = device.CreateTexture(new()
        {
            Width = size,
            Height = _height,
            Format = format,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
        });
        View = Texture.CreateView();
        Timer = new(device);
    }

    internal IGraphicsTexture Texture { get; }

    internal IGraphicsTextureView View { get; }

    internal HardwareGpuTimer Timer { get; }

    internal RenderTarget2D Target => new() { View = View, LoadOp = AttachmentLoadOp.Clear };

    internal PreparedDrawingFrame2D? Retained { get; set; }

    internal PreparedDraw2D? Legacy { get; set; }

    internal bool UnknownSubmission => _quarantined || (_submitAttempted && Submission is null);

    internal double RecordFinishMs { get; private set; }

    internal int DrawCalls { get; private set; }

    internal double EncodeMs { get; private set; }

    internal double SubmitMs { get; private set; }

    internal double CompletionMs { get; private set; }

    internal double DisposeMs { get; private set; }

    private PublicRetainedApiProbeCommands? Commands { get; set; }

    private IGraphicsSubmission? Submission { get; set; }

    public void Dispose()
    {
        if (Submission is not null || UnknownSubmission)
        {
            PublicRetainedApiProbe.QuarantinedObjects.Add(this); // Keep commands, targets, queries and frame owners alive.
            return;
        }

        Commands?.Dispose();
        if (_recorded)
        {
            Retained?.AbortUnsubmitted();
        }
        else
        {
            Retained?.Dispose();
        }

        Legacy?.Dispose();
        Timer.Dispose();
        View.Dispose();
        Texture.Dispose();
    }

    internal void EncodeSubmit(VulkanDevice device)
    {
        long start = Stopwatch.GetTimestamp();
        Commands = new(device.CreateCommandBuffer(new()));
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
        Commands.Barrier(new TextureBarrierDesc
        {
            Texture = Texture,
            Range = new(0, 1, 0, 1),
            BeforeState = _initialized ? TextureState.ColorAttachment : TextureState.Undefined,
            AfterState = TextureState.ColorAttachment,
            Before = _initialized ? color : default,
            After = color,
        });
        _initialized = true;
        if (Timer.Supported)
        {
            Timer.Begin(Commands.Inner);
        }

        long recordStart = Stopwatch.GetTimestamp();
        _recorded = true;
        if (Retained is not null)
        {
            Retained.Record(Commands);
        }
        else
        {
            Legacy!.Record(Commands);
        }

        if (Timer.Supported)
        {
            Timer.End(Commands.Inner);
        }

        Commands.Finish();
        RecordFinishMs = Ms(recordStart);
        DrawCalls = Commands.DrawCalls;
        EncodeMs = Ms(start);
        _submittedAt = Stopwatch.GetTimestamp();
        _submitAttempted = true;
        Submission = device.Queue.Submit([Commands.Inner]);
        SubmitMs = Ms(_submittedAt);
        Retained?.AttachSubmission(Submission);
    }

    internal async Task CompleteAsync(List<double>? gpu)
    {
        if (Submission is null)
        {
            return;
        }

        try
        {
            await Submission.WaitAsync();
        }
        catch
        {
            _quarantined = true;
            PublicRetainedApiProbe.QuarantinedObjects.Add(this);
            throw;
        }

        CompletionMs = Ms(_submittedAt);
        if (Submission.Status != SubmissionStatus.Completed)
        {
            _quarantined = true;
            PublicRetainedApiProbe.QuarantinedObjects.Add(this);
            throw new InvalidOperationException("Fence not completed.");
        }

        if (Timer.Supported)
        {
            double span = Timer.ReadMilliseconds();
            if (!double.IsFinite(span) || span < 0)
            {
                throw new InvalidOperationException("Invalid GPU timestamp.");
            }

            gpu?.Add(span);
        }

        long start = Stopwatch.GetTimestamp();
        try
        {
            Commands!.Dispose();
            Retained?.Retire();
            Legacy?.Dispose();
            Submission.Dispose();
            Retained?.Dispose();
        }
        catch
        {
            _quarantined = true;
            PublicRetainedApiProbe.QuarantinedObjects.Add(this);
            throw;
        }

        Commands = null;
        Submission = null;
        Retained = null;
        Legacy = null;
        _recorded = false;
        _submitAttempted = false;
        DisposeMs = Ms(start);
    }

    internal async Task<byte[]> ReadAsync(VulkanDevice device)
    {
        if (Submission is not null || UnknownSubmission)
        {
            throw new InvalidOperationException("Read requires final completed fence.");
        }

        uint pitch = _size * _pixelBytes;
        IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new()
        {
            Count = (ulong)pitch * _height,
            Usage = BufferUsage.CopyDestination,
            Memory = MemoryPreference.Readback,
        });
        IGraphicsCommandBuffer copy = device.CreateCommandBuffer(new());
        copy.Barrier(new TextureBarrierDesc
        {
            Texture = Texture,
            Range = new(0, 1, 0, 1),
            BeforeState = TextureState.ColorAttachment,
            AfterState = TextureState.CopySource,
            Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite),
            After = new(PipelineStage.Copy, ResourceAccess.CopyRead),
        });
        copy.CopyTextureToBuffer(
            new() { Texture = Texture, Width = _size, Height = _height },
            new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = _height });
        copy.Barrier(new BufferBarrierDesc<byte>
        {
            Buffer = readback.SliceElements(0, readback.Count),
            Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite),
            After = new(PipelineStage.Host, ResourceAccess.HostRead),
        });

        // Restore attachment state so repeated correctness checks can reuse this target.
        copy.Barrier(new TextureBarrierDesc
        {
            Texture = Texture,
            Range = new(0, 1, 0, 1),
            BeforeState = TextureState.CopySource,
            AfterState = TextureState.ColorAttachment,
            Before = new(PipelineStage.Copy, ResourceAccess.CopyRead),
            After = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite),
        });
        copy.Finish();
        IGraphicsSubmission submission;
        try
        {
            submission = device.Queue.Submit([copy]);
        }
        catch
        {
            _quarantined = true;
            PublicRetainedApiProbe.QuarantinedObjects.AddRange(new object[] { this, copy, readback });
            throw;
        }

        try
        {
            await submission.WaitAsync();
            if (submission.Status != SubmissionStatus.Completed)
            {
                throw new InvalidOperationException("Readback fence not completed; resources quarantined.");
            }
        }
        catch
        {
            _quarantined = true;
            PublicRetainedApiProbe.QuarantinedObjects.AddRange(new object[] { this, copy, readback, submission });
            throw;
        }

        using (submission) using (copy) using (readback)
        {
            await readback.MapAsync();
            byte[] pixels = new byte[checked((int)readback.Count)];
            readback.CopyTo(pixels);
            readback.Unmap();
            return pixels;
        }
    }

    private static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
