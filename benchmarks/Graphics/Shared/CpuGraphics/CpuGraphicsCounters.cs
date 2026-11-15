namespace Lumyte.Graphics.Benchmarks.Testing;

/// <summary>Bounded CPU-only recording observations. Completion is simulated, never GPU execution.</summary>
public readonly record struct CpuGraphicsCounters(long BufferCreates, long TextureCreates, long ShaderCreates, long PipelineCreates, long RenderPasses, long ComputePasses, long Draws, long Dispatches, long Copies, long BarrierCalls, long UploadedBytes, long Submissions, long LiveResources, ulong Checksum);
