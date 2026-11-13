using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using V = Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal static unsafe class VulkanShaderReferenceEncoding
{
    internal static byte[] Pack(VulkanDevice owner, ShaderValue value)
    {
        if (value.Reference is not IShaderReference reference)
        {
            throw new ArgumentException("A shader reference is missing.");
        }

        byte[] wire = new byte[16];
        if (reference.Resource is IGraphicsTextureView or IGraphicsSampler)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(wire, reference.Slot);
        }
        else
        {
            object handle = reference.Resource switch
            {
                IShaderDataSource source when source.Memory == MemoryPreference.Automatic && reference.BufferAccess == ShaderBufferAccess.ReadOnly => source.ShaderHandle,
                IShaderRawBuffer raw => raw.ShaderHandle,
                _ => throw new ArgumentException("A buffer reference requires GPU storage."),
            };
            var info = new BufferDeviceAddressInfo { SType = StructureType.BufferDeviceAddressInfo, Buffer = (V.Buffer)handle };
            ulong address = owner.Api.GetBufferDeviceAddress(owner.NativeDevice, &info);
            BinaryPrimitives.WriteUInt64LittleEndian(wire, checked(address + reference.OffsetInBytes));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(8), checked((uint)reference.Count));
        return wire;
    }
}
