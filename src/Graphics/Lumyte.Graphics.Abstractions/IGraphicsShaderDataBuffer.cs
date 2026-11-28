namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns typed shader storage and dependency metadata for explicit staging transfers.</summary>
/// <typeparam name="T">The application shader data structure.</typeparam>
public interface IGraphicsShaderDataBuffer<T> : IDisposable
    where T : struct, IShaderArguments
{
    /// <summary>Gets the logical element count.</summary>
    ulong Count { get; }

    /// <summary>Gets the exact target ABI allocation size.</summary>
    ulong SizeInBytes { get; }

    /// <summary>Gets the target schema element stride.</summary>
    ulong ShaderElementStrideInBytes { get; }

    /// <summary>Gets the allocation memory purpose.</summary>
    MemoryPreference Memory { get; }

    /// <summary>Gets a value indicating whether upload staging is currently mapped.</summary>
    bool IsMapped { get; }

    /// <summary>Writes values into mapped upload staging without issuing GPU commands.</summary>
    /// <param name="source">The current application values read synchronously by this explicit CPU transfer.</param>
    /// <param name="elementOffset">The first logical element.</param>
    /// <remarks>Creating buffers, slices, or registrations does not capture these values. Successful CopyFrom owns an independent staging copy; later application mutations require another CopyFrom. A command-buffer CopyBuffer reads that staging allocation, which must remain unchanged until its GPU use completes.</remarks>
    void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0);

    /// <summary>Writes an alternate codec with the same static wire contract into mapped upload staging.</summary>
    /// <typeparam name="TSource">The alternate codec type.</typeparam>
    /// <param name="source">Values synchronously encoded into independent staging bytes.</param>
    /// <param name="elementOffset">The first logical target element.</param>
    /// <remarks>Matching static metadata is necessary; matching field layout remains the codec author's precondition. TransferFields remains optional diagnostics. Mapping, idle-use, bounds, writer completion and reference fixups remain mandatory.</remarks>
    void CopyFrom<TSource>(ReadOnlySpan<TSource> source, ulong elementOffset = 0)
        where TSource : struct, IShaderArguments => throw new NotSupportedException("This shader buffer does not support alternate codecs.");

    /// <summary>Maps idle upload staging for explicit CPU writes.</summary>
    /// <param name="cancellationToken">Cancels retaining the mapping after native completion.</param>
    /// <returns>The mapping operation.</returns>
    ValueTask MapAsync(CancellationToken cancellationToken = default);

    /// <summary>Unmaps upload staging before command recording.</summary>
    void Unmap();

    /// <summary>References a nonempty logical range without extending its lifetime.</summary>
    /// <param name="offset">The first logical element.</param>
    /// <param name="count">The number of logical elements.</param>
    /// <returns>The nonowning range.</returns>
    ShaderDataSlice<T> SliceElements(ulong offset, ulong count);
}
