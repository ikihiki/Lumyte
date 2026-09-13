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
public sealed record NativeShaderInputField(string Name, NativeShaderInputFieldKind Kind, uint Offset, uint Size);
