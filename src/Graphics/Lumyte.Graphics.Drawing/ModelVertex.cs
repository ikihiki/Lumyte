using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Drawing;

/// <summary>Defines a format-independent model vertex in object space.</summary>
/// <param name="Position">The position.</param>
/// <param name="Normal">The unit surface normal.</param>
/// <param name="Tangent">The tangent and bitangent handedness.</param>
/// <param name="TexCoord">The first texture coordinate set.</param>
/// <param name="Color">The linear vertex color.</param>
[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 64)]
public readonly record struct ModelVertex(Vector3 Position, Vector3 Normal, Vector4 Tangent, Vector2 TexCoord, Vector4 Color)
{
    /// <summary>Gets the fixed byte stride shared with the model shader's natural buffer layout.</summary>
    public static int SizeInBytes => 64;

    /// <summary>Reinterprets compatible interleaved little-endian bytes without allocating or converting vertices.</summary>
    /// <param name="bytes">Complete vertices in Position/Normal/Tangent/TexCoord/Color order.</param>
    /// <returns>A borrowed vertex span; the input memory must remain alive while used.</returns>
    public static ReadOnlySpan<ModelVertex> AsVertices(ReadOnlySpan<byte> bytes)
    {
        if (!BitConverter.IsLittleEndian)
        {
            throw new PlatformNotSupportedException("Packed model vertices require a little-endian host.");
        }

        if (bytes.Length % SizeInBytes != 0)
        {
            throw new ArgumentException("Packed vertices must contain complete 64-byte elements.", nameof(bytes));
        }

        return MemoryMarshal.Cast<byte, ModelVertex>(bytes);
    }

    /// <summary>Verifies the compiled shader consumes the fixed C# vertex bytes exactly, without repacking.</summary>
    /// <param name="artifact">The model shader artifact.</param>
    /// <param name="target">The active backend target.</param>
    public static void ValidateShaderLayout(ShaderArtifact artifact, ShaderTarget target)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        var layout = ShaderDataLayout.Data(artifact.GetTarget(target), "ModelVertex");
        layout.ValidateTransferLayout(Unsafe.SizeOf<ModelVertex>(), ShaderTransfer.Fields<ModelVertex>(), "ModelVertex");
        if (!BitConverter.IsLittleEndian || Unsafe.SizeOf<ModelVertex>() != SizeInBytes || layout.Size != SizeInBytes)
        {
            throw new NotSupportedException("The compiled ModelVertex layout does not match the fixed 64-byte drawing ABI.");
        }
    }
}
