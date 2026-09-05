namespace Lumyte.Graphics.TwoD;

internal readonly record struct PathComputeJob(
    ulong InputOffset,
    ulong InputLength,
    ulong OutputOffset,
    ulong OutputLength);
