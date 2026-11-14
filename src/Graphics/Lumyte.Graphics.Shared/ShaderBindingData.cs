using System.Buffers.Binary;
using System.Text;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

/// <summary>Builds finite resource bindings for WGSL backends using native roots.</summary>
public sealed class ShaderBindingData
{
    private readonly Dictionary<object, int> _textures = [];
    private readonly Dictionary<object, int> _samplers = [];
    private readonly Dictionary<ShaderBufferBinding, int> _buffers = [];
    private readonly Dictionary<ShaderBufferBinding, int> _writable = [];

    /// <summary>Initializes a new instance of the <see cref="ShaderBindingData"/> class.</summary>
    /// <param name="dependencies">The reachable resource dependencies.</param>
    /// <param name="target">The target input.</param>
    /// <param name="caps">The caps input.</param>
    public ShaderBindingData(ShaderBindingDependencies dependencies, ShaderTargetData target, DeviceCaps caps)
    {
        Dependencies = dependencies;
        foreach (IShaderReference reference in dependencies.References)
        {
            object resource = reference.Resource;
            if (resource is IGraphicsTextureView)
            {
                _textures.TryAdd(resource, _textures.Count);
            }
            else if (resource is IGraphicsSampler)
            {
                _samplers.TryAdd(resource, _samplers.Count);
            }
            else if (reference.BufferAccess == ShaderBufferAccess.ReadOnly)
            {
                _buffers.TryAdd(BufferRange(reference, caps), _buffers.Count);
            }

            if (resource is not IGraphicsTextureView and not IGraphicsSampler && reference.BufferAccess == ShaderBufferAccess.ReadWrite)
            {
                _writable.TryAdd(BufferRange(reference, caps), _writable.Count);
            }
        }

        if (_textures.Count > caps.MaxSampledTexturesPerStage || _samplers.Count > caps.MaxSamplersPerStage || _buffers.Count + _writable.Count + (dependencies.References.Count == 0 ? 0 : 1) > caps.MaxStorageBuffersPerStage)
        {
            throw new NotSupportedException("Reachable resources exceed the enabled per-stage binding limits.");
        }

        uint capacity = dependencies.References.Count == 0 ? 0 : checked(dependencies.References.Max(r => r.Slot) + 1);
        if ((ulong)capacity * 16 > caps.MaxStorageBufferBindingSize || (ulong)capacity * 16 > int.MaxValue)
        {
            throw new NotSupportedException("The reference remap exceeds the storage binding size limit.");
        }

        Map = new byte[checked((int)capacity * 16)];
        foreach (IShaderReference reference in dependencies.References)
        {
            int offset = checked((int)reference.Slot * 16);
            if (_textures.TryGetValue(reference.Resource, out int texture))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(Map.AsSpan(offset), (uint)texture);
            }

            if (_samplers.TryGetValue(reference.Resource, out int sampler))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(Map.AsSpan(offset + 4), (uint)sampler);
            }

            var range = new ShaderBufferBinding(reference.Resource, reference.RegistrationOffsetInBytes, reference.RegistrationSizeInBytes);
            if (reference.BufferAccess == ShaderBufferAccess.ReadOnly && _buffers.TryGetValue(range, out int buffer))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(Map.AsSpan(offset + 8), (uint)buffer);
            }

            if (reference.BufferAccess == ShaderBufferAccess.ReadWrite && _writable.TryGetValue(range, out int writable))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(Map.AsSpan(offset + 12), (uint)writable);
            }
        }
    }

    /// <summary>Gets the reachable resource dependencies.</summary>
    public ShaderBindingDependencies Dependencies { get; }

    /// <summary>Gets the selected root generation metadata.</summary>
    public ShaderRootArguments RootArgs => Dependencies.Arguments;

    /// <summary>Gets the resource remapping bytes.</summary>
    public byte[] Map { get; }

    /// <summary>Gets the texture binding indices.</summary>
    public IReadOnlyDictionary<object, int> Textures => _textures;

    /// <summary>Gets the sampler binding indices.</summary>
    public IReadOnlyDictionary<object, int> Samplers => _samplers;

    /// <summary>Gets the read-only buffer binding indices.</summary>
    public IReadOnlyDictionary<ShaderBufferBinding, int> Buffers => _buffers;

    /// <summary>Gets the writable buffer binding indices.</summary>
    public IReadOnlyDictionary<ShaderBufferBinding, int> Writable => _writable;

    /// <summary>Gets the binding shape cache key.</summary>
    public string Key => $"{_textures.Count}:{_samplers.Count}:{_buffers.Count}:{_writable.Count}";

    /// <summary>Generates WGSL for the captured resource binding counts.</summary>
    /// <param name="target">The target input.</param>
    /// <returns>The processed result.</returns>
    public string Specialize(ShaderTargetData target) => WgslBindingSpecializer.Specialize(Encoding.UTF8.GetString(target.Code), _textures.Count, _samplers.Count, _buffers.Count, _writable.Count);

    /// <summary>Resolves a resource kind and pool index to a physical binding.</summary>
    /// <param name="kind">The kind input.</param>
    /// <param name="index">The index input.</param>
    /// <returns>The processed result.</returns>
    public uint Binding(string kind, int index) => kind switch
    {
        "texture" => index == 0 ? 0u : checked((uint)(10 + index - 1)),
        "sampler" => index == 0 ? 1u : checked((uint)(10 + Math.Max(0, _textures.Count - 1) + index - 1)),
        "buffer" => index == 0 ? 2u : checked((uint)(10 + Math.Max(0, _textures.Count - 1) + Math.Max(0, _samplers.Count - 1) + index - 1)),
        "writable" => index == 0 ? 3u : checked((uint)(10 + Math.Max(0, _textures.Count - 1) + Math.Max(0, _samplers.Count - 1) + Math.Max(0, _buffers.Count - 1) + index - 1)),
        _ => throw new ArgumentException("Unknown binding kind."),
    };

    private static ShaderBufferBinding BufferRange(IShaderReference reference, DeviceCaps caps)
    {
        ulong offset = reference.RegistrationOffsetInBytes;
        ulong size = reference.RegistrationSizeInBytes;
        if (offset % caps.StorageBufferOffsetAlignment != 0 || size == 0 || size % 4 != 0)
        {
            throw new ArgumentException("The registered storage range does not meet the device binding alignment.");
        }

        if (size > caps.MaxStorageBufferBindingSize)
        {
            throw new NotSupportedException("The registered storage range exceeds the device binding size limit.");
        }

        return new(reference.Resource, offset, size);
    }
}
