namespace Lumyte.Graphics.Gpu.Benchmarks;

internal sealed class FrameSample
{
    public int Frame { get; set; }

    public string InputId { get; set; } = string.Empty;

    public string Phase { get; set; } = string.Empty;

    public double RecordPublishMs { get; set; }

    public double PrepareMs { get; set; }

    public double EncodeMs { get; set; }

    public double SubmitMs { get; set; }

    public double HostFrameMs { get; set; }

    public double HostIncludingSlotMs { get; set; }

    public double SlotWaitMs { get; set; }

    public double CompletionLatencyMs { get; set; }

    public double RetirementMs { get; set; }

    public double? GpuSpanMs { get; set; }

    public int DrawCalls { get; set; }

    public int ChangedElements { get; set; }

    public int ChangedNodes { get; set; }

    public int RerecordedCommands { get; set; }

    public long CompiledRuns { get; set; }

    public long ReusedRuns { get; set; }

    public int CachedAssets { get; set; }

    public long EstimatedCachedBytes { get; set; }
}
