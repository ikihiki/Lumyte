namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns typed shader storage and dependency metadata for explicit staging transfers.</summary>
/// <typeparam name="T">The application shader data structure.</typeparam>
public interface IGraphicsShaderDataBuffer<T> : IDisposable
    where T : struct, IShaderData
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
    /// <param name="source">The values to snapshot.</param>
    /// <param name="elementOffset">The first logical element.</param>
    void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0);

    /// <summary>Certifies the generated schema and converts initial values into device-specific CPU storage.</summary>
    /// <param name="initial">Initial logical values; an empty span still certifies the complete schema.</param>
    /// <returns>Independent CPU storage that does not retain this buffer or the initial span.</returns>
    ShaderDataStorage<T> CreateStorage(ReadOnlySpan<T> initial) => throw new NotSupportedException("This backend does not implement persistent shader data storage.");

    /// <summary>Copies retained transfer bytes and freezes complete metadata into mapped upload staging.</summary>
    /// <remarks>Source and destination must have the same device and schema. Native backends do not
    /// capture or pack logical values here. Validation and snapshot allocation precede writes.
    /// Each frozen element owns at most one stride and its reference identities.</remarks>
    /// <param name="source">The device-specific persistent source.</param>
    /// <param name="sourceOffset">The first source element.</param>
    /// <param name="count">The element count, or -1 for all remaining source elements.</param>
    /// <param name="elementOffset">The first destination element; empty copies may use Count.</param>
    void CopyFrom(ShaderDataStorage<T> source, int sourceOffset = 0, int count = -1, ulong elementOffset = 0) => throw new NotSupportedException("This backend does not implement persistent shader data uploads.");

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
