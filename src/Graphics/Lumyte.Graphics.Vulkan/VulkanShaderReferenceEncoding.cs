using System.Buffers.Binary;
using Lumyte.Graphics.Abstractions;
using Silk.NET.Vulkan;
using V = Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

internal static unsafe class VulkanShaderReferenceEncoding
{
    internal static byte[] Pack(VulkanDevice owner, ShaderValue value, string kind)
    {
        if (value.Reference is not IShaderReference reference)
        {
            throw new ArgumentException("A shader reference is missing.");
        }

        byte[] wire = new byte[16];
        if (kind is "GpuTextureRef" or "GpuSamplerRef")
        {
            BinaryPrimitives.WriteUInt32LittleEndian(wire, reference.Slot);
        }
        else
        {
            object handle = reference.Resource switch
            {
                IShaderDataSource source when source.Memory == MemoryPreference.Automatic && kind == "GpuBufferRef" => source.ShaderHandle,
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

    // Validate only manufacture, device, immutable access and range facts. Registration
    // release/replacement and GPU lifetime remain caller-managed; no generation scan is added.
    internal static int EncodePersistent(VulkanDevice owner, ShaderDataMember member, object value, string kind, Span<byte> destination)
    {
        if (destination.Length != 16)
        {
            throw new ArgumentException("A backend reference must occupy exactly 16 bytes.", nameof(destination));
        }

        if (value is not IBackendShaderReference reference ||
            reference.Table is not VulkanArgumentTable table || !table.BelongsTo(owner))
        {
            throw new ArgumentException("A backend-created shader reference from the same device is required.");
        }

        switch (kind)
        {
            case "GpuTextureRef":
                if (member.ValueType != typeof(IGraphicsTextureView))
                {
                    throw new ArgumentException("The texture reference target does not match the compiled field.");
                }

                if (reference.Resource is not VulkanTextureView view || !ReferenceEquals(view.Owner, owner) ||
                    (view.Texture.Usage & TextureUsage.Sampled) == 0)
                {
                    throw new ArgumentException("A sampled texture view from the same device is required.");
                }

                if (view.Info.Dimension != TextureViewDimension.D2 || view.Info.ArrayLayerCount != 1 ||
                    view.Info.Format is not (
                        TextureFormat.Rgba8Unorm or TextureFormat.Rgba8Srgb or
                        TextureFormat.Bgra8Unorm or TextureFormat.Bgra8Srgb or
                        TextureFormat.R8Unorm or TextureFormat.Rg8Unorm or
                        TextureFormat.R16Float or TextureFormat.Rg16Float or
                        TextureFormat.Rgba16Float or TextureFormat.Rgb10A2Unorm))
                {
                    throw new NotSupportedException("Shader texture references require filterable Texture2D color views.");
                }

                break;
            case "GpuSamplerRef":
                if (member.ValueType != typeof(IGraphicsSampler))
                {
                    throw new ArgumentException("The sampler reference target does not match the compiled field.");
                }

                if (reference.Resource is not VulkanSampler sampler || !ReferenceEquals(sampler.Owner, owner))
                {
                    throw new ArgumentException("A sampler from the same device is required.");
                }

                if (sampler.Desc.Compare.HasValue)
                {
                    throw new NotSupportedException("Shader sampler references do not support comparison sampling.");
                }

                break;
            case "GpuBufferRef":
            case "GpuRWBufferRef":
                ulong size;
                if (reference.Resource is IShaderDataSource data && kind == "GpuBufferRef" && data.Memory == MemoryPreference.Automatic)
                {
                    if (data.Layout.Name != member.ReferenceTarget)
                    {
                        throw new ArgumentException("The shader data reference target does not match the compiled field.");
                    }

                    size = data.SizeInBytes;
                }
                else if (reference.Resource is IShaderRawBuffer raw &&
                    (raw.Usage & (kind == "GpuBufferRef" ? BufferUsage.ShaderRead : BufferUsage.ShaderWrite)) != 0)
                {
                    size = raw.SizeInBytes;
                }
                else
                {
                    throw new ArgumentException("The buffer resource or shader access does not match the compiled field.");
                }

                if (reference.Count == 0 || reference.SizeInBytes == 0 ||
                    reference.RegistrationOffsetInBytes > size || reference.RegistrationSizeInBytes > size - reference.RegistrationOffsetInBytes ||
                    reference.OffsetInBytes < reference.RegistrationOffsetInBytes || reference.OffsetInBytes > size ||
                    reference.OffsetInBytes - reference.RegistrationOffsetInBytes > reference.RegistrationSizeInBytes ||
                    reference.SizeInBytes > reference.RegistrationSizeInBytes - (reference.OffsetInBytes - reference.RegistrationOffsetInBytes))
                {
                    throw new ArgumentException("The shader reference lies outside its registered buffer range.");
                }

                break;
            default:
                throw new NotSupportedException("The compiled shader reference kind is unsupported.");
        }

        destination.Clear();
        if (kind is "GpuTextureRef" or "GpuSamplerRef")
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination, reference.Slot);
        }
        else
        {
            if (reference.Resource is not IVulkanBufferHandle buffer)
            {
                throw new ArgumentException("A buffer reference requires backend GPU storage.");
            }

            var info = new BufferDeviceAddressInfo { SType = StructureType.BufferDeviceAddressInfo, Buffer = buffer.NativeBuffer };
            ulong address = owner.Api.GetBufferDeviceAddress(owner.NativeDevice, &info);
            BinaryPrimitives.WriteUInt64LittleEndian(destination, checked(address + reference.OffsetInBytes));
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], checked((uint)reference.Count));
        return 16;
    }
}
