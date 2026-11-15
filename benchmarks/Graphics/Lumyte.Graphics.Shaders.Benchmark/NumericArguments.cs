using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shaders.Benchmark;

/// <summary>Current generated C#-authoritative numeric bulk transfer.</summary>
public partial struct NumericArguments : IShaderArguments
{
    /// <summary>The vector is intentionally three-component, without shader-inferred padding.</summary>
    public Vector3 Position;

    /// <summary>Complete row-major matrix bytes.</summary>
    public Matrix4x4 Transform;

    /// <summary>Trailing scalar.</summary>
    public uint Tag;
}
