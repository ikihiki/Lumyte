namespace Lumyte.Graphics.Pipelines;

/// <summary>Opts a compiled pipeline into finite, completion-published canonical upload retention.</summary>
public sealed record GraphicsImmutableUploadPolicy
{
    /// <summary>Initializes a new instance of the <see cref="GraphicsImmutableUploadPolicy"/> class.</summary>
    /// <param name="maxBytes">The maximum retained native payload bytes, including active unpublished versions.</param>
    /// <param name="maxChunks">The maximum retained native payload owners.</param>
    /// <param name="chunkBytes">The maximum canonical bytes in each immutable invalidation unit.</param>
    public GraphicsImmutableUploadPolicy(long maxBytes, int maxChunks, int chunkBytes = 65536)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxChunks);
        ArgumentOutOfRangeException.ThrowIfLessThan(chunkBytes, 16);
        MaxBytes = maxBytes;
        MaxChunks = maxChunks;
        ChunkBytes = chunkBytes;
    }

    /// <summary>Gets the finite native payload ceiling; active owners are never evicted.</summary>
    public long MaxBytes { get; }

    /// <summary>Gets the finite native owner ceiling.</summary>
    public int MaxChunks { get; }

    /// <summary>Gets the canonical chunk size ceiling.</summary>
    public int ChunkBytes { get; }
}
