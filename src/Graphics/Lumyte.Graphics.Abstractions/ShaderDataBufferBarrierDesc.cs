namespace Lumyte.Graphics.Abstractions;

/// <summary>Describes an explicit shader data buffer range dependency.</summary>
/// <typeparam name="T">The application shader data structure.</typeparam>
public sealed record ShaderDataBufferBarrierDesc<T>
    where T : struct, IShaderData
{
    /// <summary>Gets the shader data range.</summary>
    public required ShaderDataSlice<T> Buffer { get; init; }

    /// <summary>Gets the preceding accesses.</summary>
    public required BarrierScope Before { get; init; }

    /// <summary>Gets the subsequent accesses.</summary>
    public required BarrierScope After { get; init; }
}
