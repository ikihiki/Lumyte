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

    /// <summary>Maps idle upload staging for explicit CPU writes.</summary>
    /// <returns>The mapping operation.</returns>
    ValueTask MapAsync();

    /// <summary>Unmaps upload staging before command recording.</summary>
    void Unmap();

    /// <summary>References a nonempty logical range without extending its lifetime.</summary>
    /// <param name="offset">The first logical element.</param>
    /// <param name="count">The number of logical elements.</param>
    /// <returns>The nonowning range.</returns>
    ShaderDataSlice<T> SliceElements(ulong offset, ulong count);
}
