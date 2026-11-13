using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal static class Direct3D12ShaderReferenceEncoding
{
    internal static byte[] Pack(Direct3D12Device owner, ShaderValue value)
    {
        owner.RequireReady();
        if (value.Reference is not IShaderReference reference)
        {
            throw new ArgumentException("A shader reference is missing.");
        }

        byte[] wire = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(wire, reference.Slot);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(4), checked((uint)reference.OffsetInBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(8), checked((uint)reference.Count));
        return wire;
    }
}
