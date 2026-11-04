namespace Lumyte.Graphics.Gpu.Benchmarks;

internal sealed class SuiteSample
{
    public int Frame { get; set; }

    public string InputId { get; set; } = string.Empty;

    public double SlotWaitMs { get; set; }

    public double HostMs { get; set; }

    public double MapCopyMs { get; set; }

    public double PrepareMs { get; set; }

    public double EncodeMs { get; set; }

    public double SubmitMs { get; set; }

    public double CompletionMs { get; set; }

    public double? GpuMs { get; set; }

    public double UploadHostMs { get; set; }

    public double? UploadGpuMs { get; set; }

    public uint BytesPerRow { get; set; }

    public long MappedBytes { get; set; }

    public long GpuUploadBytes { get; set; }

    public long GpuReadbackBytes { get; set; }

    public int CopyCalls { get; set; }

    public int DrawCalls { get; set; }
}
