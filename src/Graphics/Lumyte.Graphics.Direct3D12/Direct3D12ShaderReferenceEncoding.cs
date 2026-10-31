using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Direct3D12;

internal static class Direct3D12ShaderReferenceEncoding
{
    internal static byte[] Pack(Direct3D12Device owner, ShaderValue value, string kind)
    {
        owner.RequireReady();
        if (value.Reference is not IShaderReference reference)
        {
            throw new ArgumentException("A shader reference is missing.");
        }

        bool valid = kind switch
        {
            "GpuTextureRef" => reference.Resource is Direct3D12TextureView,
            "GpuSamplerRef" => reference.Resource is Direct3D12Sampler,
            "GpuBufferRef" => reference.Resource is IShaderDataSource { Memory: MemoryPreference.Automatic } || (reference.Resource is IShaderRawBuffer raw && (raw.Usage & BufferUsage.ShaderRead) != 0),
            "GpuRWBufferRef" => reference.Resource is IShaderRawBuffer raw && (raw.Usage & BufferUsage.ShaderWrite) != 0,
            _ => false,
        };
        if (!valid)
        {
            throw new ArgumentException("The shader reference type or buffer access does not match the compiled field.");
        }

        byte[] wire = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(wire, reference.Slot);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(4), checked((uint)reference.OffsetInBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(8), checked((uint)reference.Count));
        return wire;
    }
}
