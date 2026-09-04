namespace Lumyte.Graphics.TwoD;

public readonly record struct SceneUpdateStatistics(
    int UpdatedNodeCount,
    ulong UploadedByteCount,
    bool BufferReallocated);
