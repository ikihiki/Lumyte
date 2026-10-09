using System.Buffers.Binary;
using System.Text;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics;

internal sealed class ShaderBindingData
{
    private readonly Dictionary<object, uint> _identities = [];
    private readonly Dictionary<object, int> _textures = [];
    private readonly Dictionary<object, int> _samplers = [];
    private readonly Dictionary<object, int> _buffers = [];
    private readonly Dictionary<object, int> _writable = [];

    internal ShaderBindingData(ShaderBindingSnapshot snapshot, ShaderTargetData target, DeviceCaps caps)
    {
        Snapshot = snapshot;
        snapshot.ValidateLayouts(target);
        var rootLayout = ShaderDataLayout.Root(target, snapshot.Root.RootParameter);
        var read = new HashSet<object>();
        var write = new HashSet<object>();
        void Uses(ShaderDataLayout layout, ShaderValueSnapshot values)
        {
            foreach (ShaderValue value in values.Values)
            {
                if (value.Reference is IShaderReference reference)
                {
                    string kind = layout.ReferenceKind(value.Path);
                    if (kind == "GpuBufferRef")
                    {
                        read.Add(reference.Resource);
                    }
                    else if (kind == "GpuRWBufferRef")
                    {
                        write.Add(reference.Resource);
                    }
                }
            }
        }

        Uses(rootLayout, snapshot.Root);
        foreach (((IShaderDataSource source, ulong _), ShaderValueSnapshot values) in snapshot.Elements)
        {
            Uses(source.Layout, values);
        }

        foreach (IShaderReference reference in snapshot.References)
        {
            object resource = reference.Resource;
            _identities.TryAdd(resource, checked((uint)_identities.Count));
            if (resource is IGraphicsTextureView)
            {
                _textures.TryAdd(resource, _textures.Count);
            }
            else if (resource is IGraphicsSampler)
            {
                _samplers.TryAdd(resource, _samplers.Count);
            }
            else if (read.Contains(resource))
            {
                _buffers.TryAdd(resource, _buffers.Count);
            }

            if (write.Contains(resource))
            {
                _writable.TryAdd(resource, _writable.Count);
            }
        }

        if (_textures.Count > caps.MaxSampledTexturesPerStage || _samplers.Count > caps.MaxSamplersPerStage || _buffers.Count + _writable.Count + (snapshot.References.Count == 0 ? 0 : 1) > caps.MaxStorageBuffersPerStage || caps.MaxUniformBuffersPerStage < 1)
        {
            throw new NotSupportedException("Reachable resources exceed the enabled per-stage binding limits.");
        }

        Root = rootLayout.Pack(snapshot.Root, ReferenceBytes);
        uint capacity = checked((uint)_identities.Count);
        Map = new byte[checked((int)capacity * 16)];
        foreach (IShaderReference reference in snapshot.References)
        {
            int offset = checked((int)_identities[reference.Resource] * 16);
            if (_textures.TryGetValue(reference.Resource, out int texture))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(Map.AsSpan(offset), (uint)texture);
            }

            if (_samplers.TryGetValue(reference.Resource, out int sampler))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(Map.AsSpan(offset + 4), (uint)sampler);
            }

            if (_buffers.TryGetValue(reference.Resource, out int buffer))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(Map.AsSpan(offset + 8), (uint)buffer);
            }

            if (_writable.TryGetValue(reference.Resource, out int writable))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(Map.AsSpan(offset + 12), (uint)writable);
            }
        }

        foreach ((IShaderDataSource source, ulong element) in snapshot.Elements.Keys)
        {
            if (!Data.TryGetValue(source, out byte[]? bytes))
            {
                if (source.SizeInBytes > caps.MaxStorageBufferBindingSize || source.SizeInBytes > int.MaxValue)
                {
                    throw new NotSupportedException("Shader data backing exceeds the storage binding size limit.");
                }

                bytes = new byte[checked((int)source.SizeInBytes)];
                Data.Add(source, bytes);
            }

            source.Layout.Pack(snapshot.Elements[(source, element)], ReferenceBytes).CopyTo(bytes, checked((int)(element * (ulong)source.Layout.Size)));
        }
    }

    internal ShaderBindingSnapshot Snapshot { get; }

    internal byte[] Root { get; }

    internal byte[] Map { get; }

    internal Dictionary<IShaderDataSource, byte[]> Data { get; } = [];

    internal IReadOnlyDictionary<object, int> Textures => _textures;

    internal IReadOnlyDictionary<object, int> Samplers => _samplers;

    internal IReadOnlyDictionary<object, int> Buffers => _buffers;

    internal IReadOnlyDictionary<object, int> Writable => _writable;

    internal string Key => $"{_textures.Count}:{_samplers.Count}:{_buffers.Count}:{_writable.Count}";

    internal string Specialize(ShaderTargetData target) => WgslBindingSpecializer.Specialize(Encoding.UTF8.GetString(target.Code), _textures.Count, _samplers.Count, _buffers.Count, _writable.Count);

    internal uint Binding(string kind, int index) => kind switch
    {
        "texture" => index == 0 ? 0u : checked((uint)(10 + index - 1)),
        "sampler" => index == 0 ? 1u : checked((uint)(10 + Math.Max(0, _textures.Count - 1) + index - 1)),
        "buffer" => index == 0 ? 2u : checked((uint)(10 + Math.Max(0, _textures.Count - 1) + Math.Max(0, _samplers.Count - 1) + index - 1)),
        "writable" => index == 0 ? 3u : checked((uint)(10 + Math.Max(0, _textures.Count - 1) + Math.Max(0, _samplers.Count - 1) + Math.Max(0, _buffers.Count - 1) + index - 1)),
        _ => throw new ArgumentException("Unknown binding kind."),
    };

    private byte[] ReferenceBytes(ShaderValue value, string kind)
    {
        if (value.Reference is not IShaderReference reference)
        {
            throw new ArgumentException("Missing resource reference.");
        }

        object resource = reference.Resource;
        bool compatible = kind switch
        {
            "GpuTextureRef" => resource is IGraphicsTextureView,
            "GpuSamplerRef" => resource is IGraphicsSampler,
            "GpuBufferRef" => resource is IShaderDataSource || (resource is IShaderRawBuffer input && (input.Usage & BufferUsage.ShaderRead) != 0),
            "GpuRWBufferRef" => resource is IShaderRawBuffer output && (output.Usage & BufferUsage.ShaderWrite) != 0,
            _ => false,
        };
        if (!compatible)
        {
            throw new ArgumentException("Reference kind does not match the compiled shader member.");
        }

        byte[] wire = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(wire, _identities[resource]);
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(4), checked((uint)reference.OffsetInBytes));
        BinaryPrimitives.WriteUInt32LittleEndian(wire.AsSpan(8), checked((uint)reference.Count));
        return wire;
    }
}
