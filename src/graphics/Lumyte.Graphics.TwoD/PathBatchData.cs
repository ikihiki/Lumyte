namespace Lumyte.Graphics.TwoD;

internal readonly record struct PathBatchData(
    byte[] InputBytes,
    ulong OutputLength,
    uint TileCount,
    uint EdgeCapacity);
