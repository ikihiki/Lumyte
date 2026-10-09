namespace Lumyte.Graphics.Abstractions;

/// <summary>Owns CPU-set shader values and their dependency metadata without raw GPU copy access.</summary>
/// <typeparam name="T">The application shader data structure.</typeparam>
public interface IGraphicsShaderDataBuffer<T> : IDisposable
    where T : struct, IShaderData
{
    /// <summary>Gets the logical element count.</summary>
    ulong Count { get; }

    /// <summary>Gets the target ABI size, excluding private backing allocations.</summary>
    ulong SizeInBytes { get; }

    /// <summary>Gets the target schema element stride.</summary>
    ulong ShaderElementStrideInBytes { get; }

    /// <summary>Replaces a CPU value range after validation without mapping, GPU commands or waiting.</summary>
    /// <param name="source">The values to snapshot.</param>
    /// <param name="elementOffset">The first logical element.</param>
    void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0);

    /// <summary>References a nonempty logical range without extending its lifetime.</summary>
    /// <param name="offset">The first logical element.</param>
    /// <param name="count">The number of logical elements.</param>
    /// <returns>The nonowning range.</returns>
    ShaderDataSlice<T> SliceElements(ulong offset, ulong count);
}
