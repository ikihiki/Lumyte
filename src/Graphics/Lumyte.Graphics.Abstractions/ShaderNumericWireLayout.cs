namespace Lumyte.Graphics.Abstractions;

/// <summary>Declares an explicitly laid-out C# numeric GPU element.</summary>
public sealed class ShaderNumericWireLayout
{
    /// <summary>Initializes a new instance of the <see cref="ShaderNumericWireLayout"/> class.</summary>
    /// <param name="stride">The exact managed storage stride.</param>
    /// <param name="members">Every numeric C# field, including explicit padding fields consumed by the shader.</param>
    public ShaderNumericWireLayout(int stride, params ShaderNumericWireMember[] members)
    {
        ArgumentNullException.ThrowIfNull(members);
        Stride = stride;
        Members = Array.AsReadOnly((ShaderNumericWireMember[])members.Clone());
    }

    /// <summary>Gets the C# element stride.</summary>
    public int Stride { get; }

    /// <summary>Gets the C# numeric member offsets, types and byte lengths.</summary>
    public IReadOnlyList<ShaderNumericWireMember> Members { get; }
}
