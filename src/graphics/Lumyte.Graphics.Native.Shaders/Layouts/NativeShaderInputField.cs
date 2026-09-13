namespace Lumyte.Graphics.Native.Shaders;

public enum NativeShaderInputFieldKind
{
    Scalar,
    Vector,
    Matrix,
    GpuAddress,
    DescriptorIndex,
}

/// <summary>Byte placement metadata. A reference field does not own or enumerate the referenced resource.</summary>
public sealed record NativeShaderInputField(string Name, NativeShaderInputFieldKind Kind, uint Offset, uint Size,
    NativeShaderResourceKind ResourceKind = NativeShaderResourceKind.None);

/// <summary>Explicit shader author metadata for host reference generation, never pointer traversal.</summary>
public enum NativeShaderResourceKind
{
    None,
    Buffer,
    View,
    Sampler,
}
