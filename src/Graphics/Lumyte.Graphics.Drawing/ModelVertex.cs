using System.Numerics;
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
public readonly partial record struct ModelVertex(Vector3 Position, Vector3 Normal, Vector4 Tangent, Vector2 TexCoord, Vector4 Color) : IShaderData
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

        // Distinct sentinel components detect changed offsets, element sizes and member ordering.
        var probe = new ModelVertex(new(1, 2, 3), new(4, 5, 6), new(7, 8, 9, 10), new(11, 12), new(13, 14, 15, 16));
        ReadOnlySpan<byte> raw = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref probe, 1));
        var layout = ShaderDataLayout.Data(artifact.GetTarget(target), "ModelVertex");
        byte[] packed = layout.Pack(IShaderArguments.Capture(in probe), (_, _) => throw new NotSupportedException("Model vertices cannot contain GPU references."));
        if (!BitConverter.IsLittleEndian || raw.Length != SizeInBytes || layout.Size != SizeInBytes || !raw.SequenceEqual(packed))
        {
            throw new NotSupportedException("The compiled ModelVertex layout does not match the fixed 64-byte drawing ABI.");
        }
    }
}
