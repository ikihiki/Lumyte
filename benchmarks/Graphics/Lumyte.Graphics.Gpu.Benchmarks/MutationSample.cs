namespace Lumyte.Graphics.Gpu.Benchmarks;

internal sealed class MutationSample
{
    public int Frame { get; set; }

    public string InputId { get; set; } = string.Empty;

    public double UpdateMs { get; set; }

    public double PrepareMs { get; set; }

    public double HostFrameMs { get; set; }

    public double EncodeMs { get; set; }

    public double SubmitMs { get; set; }

    public double SlotWaitMs { get; set; }

    public double CompletionLatencyMs { get; set; }

    public double RetirementMs { get; set; }

    public double? GpuSpanMs { get; set; }

    public int DrawCalls { get; set; }

    public int PropertyEdits { get; set; }

    public int StructuralEdits { get; set; }

    public int Elements { get; set; }

    public long CompiledRuns { get; set; }

    public long ReusedRuns { get; set; }

    public int CachedAssets { get; set; }

    public long EstimatedCachedBytes { get; set; }
}
