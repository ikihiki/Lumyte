using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuShaderBinding : IDisposable
{
    private readonly List<nint> _buffers = [];
    private readonly ShaderBindingData _data;
    private readonly WgpuArgumentTable? _table;
    private readonly IReadOnlyCollection<IShaderReference> _registrations;
    private WGPUBindGroupImpl* _native;

    internal WgpuShaderBinding(WgpuDevice owner, ShaderBindingData data, WgpuRootStorage? rootStorage, WGPUBindGroupLayoutImpl* layout, WgpuArgumentTable? table, IReadOnlyCollection<IShaderReference> registrations)
    {
        _data = data;
        _table = table;
        _registrations = registrations;
        if (rootStorage != null && owner.Caps.MaxUniformBuffersPerStage < 1)
        {
            throw new NotSupportedException("Root uniform emulation exceeds the enabled per-stage binding limits.");
        }

        if ((rootStorage != null && (ulong)rootStorage.Size > owner.Limits.maxUniformBufferBindingSize) || (ulong)data.Map.Length > owner.Caps.MaxStorageBufferBindingSize)
        {
            throw new NotSupportedException("Internal root or remap binding exceeds the device size limit.");
        }

        try
        {
            var entries = new List<WGPUBindGroupEntry>();
            if (rootStorage != null)
            {
                rootStorage.Seal();
                entries.Add(new() { binding = 8, buffer = rootStorage.Native, size = (ulong)rootStorage.Size });
            }

            if (data.Map.Length != 0)
            {
                WGPUBufferImpl* map = Buffer(owner, data.Map, A.BufferUsage.Storage);
                entries.Add(new() { binding = 9, buffer = map, size = (ulong)data.Map.Length });
            }

            foreach ((object resource, int index) in data.Textures)
            {
                entries.Add(new() { binding = data.Binding("texture", index), textureView = ((WgpuTextureView)resource).Native.Handle });
            }

            foreach ((object resource, int index) in data.Samplers)
            {
                entries.Add(new() { binding = data.Binding("sampler", index), sampler = ((WgpuSampler)resource).Native });
            }

            foreach ((string kind, IReadOnlyDictionary<ShaderBufferBinding, int> resources) in new[] { ("buffer", data.Buffers), ("writable", data.Writable) })
            {
                foreach ((ShaderBufferBinding range, int index) in resources)
                {
                    nint buffer = range.Resource is IShaderRawBuffer raw ? (nint)raw.ShaderHandle : (nint)((IShaderDataSource)range.Resource).ShaderHandle;
                    entries.Add(new() { binding = data.Binding(kind, index), buffer = (WGPUBufferImpl*)buffer, offset = range.OffsetInBytes, size = range.SizeInBytes });
                }
            }

            WGPUBindGroupEntry[] array = entries.ToArray();
            fixed (WGPUBindGroupEntry* pointer = array)
            {
                var descriptor = new WGPUBindGroupDescriptor { layout = layout, entries = pointer, entryCount = (nuint)array.Length };
                _native = WGPU.wgpuDeviceCreateBindGroup(owner.NativeDevice.Handle, &descriptor);
                if (_native == null)
                {
                    throw new InvalidOperationException("WebGPU shader bind group creation failed.");
                }
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal WGPUBindGroupImpl* Native => _native;

    public void Dispose()
    {
        if (_native != null)
        {
            WGPU.wgpuBindGroupRelease(_native);
            _native = null;
        }

        foreach (nint buffer in _buffers)
        {
            WGPU.wgpuBufferRelease((WGPUBufferImpl*)buffer);
        }

        _buffers.Clear();
    }

    internal static WGPUPipelineLayoutImpl* CreateLayout(WgpuDevice owner, ShaderBindingData data, bool includeRoot, bool compute = false)
    {
        if ((includeRoot && owner.Limits.maxUniformBuffersPerShaderStage < 1) ||
            (ulong)data.Map.Length > owner.Limits.maxBufferSize ||
            (ulong)data.Map.Length > owner.Limits.maxStorageBufferBindingSize ||
            (uint)data.Textures.Count > owner.Limits.maxSampledTexturesPerShaderStage ||
            (uint)data.Samplers.Count > owner.Limits.maxSamplersPerShaderStage ||
            (uint)(data.Buffers.Count + data.Writable.Count + (data.Map.Length == 0 ? 0 : 1)) > owner.Limits.maxStorageBuffersPerShaderStage)
        {
            throw new NotSupportedException("Argument table exceeds actual device binding limits.");
        }

        ulong visibility = (ulong)(compute ? A.ShaderStage.Compute : A.ShaderStage.Vertex | A.ShaderStage.Fragment);
        var entries = new List<WGPUBindGroupLayoutEntry>();
        if (includeRoot)
        {
            entries.Add(new() { binding = 8, visibility = visibility, buffer = new() { type = WGPUBufferBindingType.Uniform } });
        }

        if (data.Map.Length != 0)
        {
            entries.Add(new() { binding = 9, visibility = visibility, buffer = new() { type = WGPUBufferBindingType.ReadOnlyStorage } });
        }

        foreach ((object _, int index) in data.Textures)
        {
            entries.Add(new() { binding = data.Binding("texture", index), visibility = visibility, texture = new() { sampleType = WGPUTextureSampleType.Float, viewDimension = WGPUTextureViewDimension._2D } });
        }

        foreach ((object _, int index) in data.Samplers)
        {
            entries.Add(new() { binding = data.Binding("sampler", index), visibility = visibility, sampler = new() { type = WGPUSamplerBindingType.Filtering } });
        }

        foreach ((string kind, IReadOnlyDictionary<ShaderBufferBinding, int> resources) in new[] { ("buffer", data.Buffers), ("writable", data.Writable) })
        {
            foreach ((ShaderBufferBinding _, int index) in resources)
            {
                entries.Add(new() { binding = data.Binding(kind, index), visibility = kind == "writable" && !compute ? (ulong)A.ShaderStage.Fragment : visibility, buffer = new() { type = kind == "buffer" ? WGPUBufferBindingType.ReadOnlyStorage : WGPUBufferBindingType.Storage } });
            }
        }

        if ((uint)entries.Count > owner.Limits.maxBindingsPerBindGroup || entries.Any(entry => entry.binding >= owner.Limits.maxBindingsPerBindGroup))
        {
            throw new NotSupportedException("Shader arguments exceed the bind group entry limit.");
        }

        WGPUBindGroupLayoutEntry[] array = entries.ToArray();
        fixed (WGPUBindGroupLayoutEntry* pointer = array)
        {
            var descriptor = new WGPUBindGroupLayoutDescriptor { entries = pointer, entryCount = (nuint)array.Length };
            WGPUBindGroupLayoutImpl* group = WGPU.wgpuDeviceCreateBindGroupLayout(owner.NativeDevice.Handle, &descriptor);
            try
            {
                var pipeline = new WGPUPipelineLayoutDescriptor { bindGroupLayouts = &group, bindGroupLayoutCount = 1 };
                return WGPU.wgpuDeviceCreatePipelineLayout(owner.NativeDevice.Handle, &pipeline);
            }
            finally
            {
                WGPU.wgpuBindGroupLayoutRelease(group);
            }
        }
    }

    private WGPUBufferImpl* Buffer(WgpuDevice owner, byte[] bytes, A.BufferUsage usage)
    {
        var descriptor = new WGPUBufferDescriptor { size = checked(((ulong)bytes.Length + 3) & ~3UL), usage = (ulong)usage, mappedAtCreation = 1 };
        WGPUBufferImpl* native = WGPU.wgpuDeviceCreateBuffer(owner.NativeDevice.Handle, &descriptor);
        if (native == null)
        {
            throw new InvalidOperationException("WebGPU shader backing allocation failed.");
        }

        _buffers.Add((nint)native);
        void* pointer = WGPU.wgpuBufferGetMappedRange(native, 0, (nuint)descriptor.size);
        if (pointer == null)
        {
            throw new InvalidOperationException("WebGPU shader backing mapping failed.");
        }

        var span = new Span<byte>(pointer, checked((int)descriptor.size));
        span.Clear();
        bytes.CopyTo(span);
        WGPU.wgpuBufferUnmap(native);
        return native;
    }
}
