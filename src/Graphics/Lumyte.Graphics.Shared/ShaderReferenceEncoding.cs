using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Encodes stable logical slots for WGSL resource references.</summary>
public static class ShaderReferenceEncoding
{
    /// <summary>Packs a validated reference without draw-dependent resource indices.</summary>
    /// <param name="value">The reference value.</param>
    /// <param name="kind">The compiled reference kind.</param>
    /// <returns>The helper ABI bytes.</returns>
    public static byte[] Pack(ShaderValue value, string kind)
    {
        if (value.Reference is not IShaderReference reference)
        {
            throw new ArgumentException("A shader reference is missing.");
        }

        reference.Validate();
        if (reference.Resource is IShaderDataSource data && data.Memory != MemoryPreference.Automatic)
        {
            throw new ArgumentException("Upload staging cannot be referenced by a shader.");
        }

        byte[] wire = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(wire, reference.Slot);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(4), checked((uint)(reference.OffsetInBytes - reference.RegistrationOffsetInBytes)));
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(8), checked((uint)reference.Count));
        return wire;
    }
}
