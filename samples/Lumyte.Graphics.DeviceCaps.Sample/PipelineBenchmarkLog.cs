using Lumyte.Graphics.Samples;

internal sealed record PipelineBenchmarkLog(int Round, bool Warmup, bool Cache, PipelineBenchmarkResult Result);
