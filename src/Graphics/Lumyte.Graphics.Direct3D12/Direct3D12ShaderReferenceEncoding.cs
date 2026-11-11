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

    // Validate only manufacture, device, immutable access and range facts. Registration
    // release/replacement and GPU lifetime remain caller-managed; no generation scan is added.
    internal static int EncodePersistent(Direct3D12Device owner, ShaderDataMember member, object value, string kind, Span<byte> destination)
    {
        if (destination.Length != 16)
        {
            throw new ArgumentException("A backend reference must occupy exactly 16 bytes.", nameof(destination));
        }

        if (value is not IBackendShaderReference reference ||
            reference.Table is not Direct3D12ArgumentTable table || !table.BelongsTo(owner))
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

                if (reference.Resource is not Direct3D12TextureView view || !ReferenceEquals(view.Owner, owner) ||
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

                if (reference.Slot >= 128 || view.Info.BaseArrayLayer != 0 || view.Texture.ArrayLayers != 1)
                {
                    throw new NotSupportedException("The shader helper supports single-layer Texture2D views in slots 0..127.");
                }

                break;
            case "GpuSamplerRef":
                if (member.ValueType != typeof(IGraphicsSampler))
                {
                    throw new ArgumentException("The sampler reference target does not match the compiled field.");
                }

                if (reference.Resource is not Direct3D12Sampler sampler || !ReferenceEquals(sampler.Owner, owner))
                {
                    throw new ArgumentException("A sampler from the same device is required.");
                }

                if (reference.Slot >= 16)
                {
                    throw new NotSupportedException("Sampler slot exceeds 15.");
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

                if (reference.Slot >= 64 || size % 4 != 0)
                {
                    throw new NotSupportedException("Raw shader storage requires slots 0..63 and a size divisible by four.");
                }

                break;
            default:
                throw new NotSupportedException("The compiled shader reference kind is unsupported.");
        }

        owner.RequireReady();
        destination.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(destination, reference.Slot);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], checked((uint)reference.OffsetInBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], checked((uint)reference.Count));
        return 16;
    }
}
