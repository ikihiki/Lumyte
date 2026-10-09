using Ahjo.Wgpu.Native;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class WgpuShaderBinding : IDisposable
{
    private readonly List<nint> _buffers = [];
    private WGPUBindGroupImpl* _native;

    internal WgpuShaderBinding(WgpuDevice owner, ShaderBindingData data, WGPUBindGroupLayoutImpl* layout)
    {
        if ((ulong)data.Root.Length > owner.Limits.maxUniformBufferBindingSize || (ulong)data.Map.Length > owner.Caps.MaxStorageBufferBindingSize)
        {
            throw new NotSupportedException("Internal root or remap binding exceeds the device size limit.");
        }

        try
        {
            var entries = new List<WGPUBindGroupEntry>();
            WGPUBufferImpl* root = Buffer(owner, data.Root, A.BufferUsage.Uniform);
            entries.Add(new() { binding = 8, buffer = root, size = (ulong)data.Root.Length });
            if (data.Map.Length != 0)
            {
                WGPUBufferImpl* map = Buffer(owner, data.Map, A.BufferUsage.Storage);
                entries.Add(new() { binding = 9, buffer = map, size = (ulong)data.Map.Length });
            }

            var backing = new Dictionary<object, nint>();
            foreach ((IShaderDataSource source, byte[] bytes) in data.Data)
            {
                backing.Add(source, (nint)Buffer(owner, bytes, A.BufferUsage.Storage));
            }

            foreach ((object resource, int index) in data.Textures)
            {
                entries.Add(new() { binding = data.Binding("texture", index), textureView = ((WgpuTextureView)resource).Native.Handle });
            }

            foreach ((object resource, int index) in data.Samplers)
            {
                entries.Add(new() { binding = data.Binding("sampler", index), sampler = ((WgpuSampler)resource).Native });
            }

            foreach ((string kind, IReadOnlyDictionary<object, int> resources) in new[] { ("buffer", data.Buffers), ("writable", data.Writable) })
            {
                foreach ((object resource, int index) in resources)
                {
                    nint buffer = resource is IShaderRawBuffer raw ? (nint)raw.ShaderHandle : backing[resource];
                    ulong size = resource is IShaderRawBuffer storage ? storage.SizeInBytes : (ulong)data.Data[(IShaderDataSource)resource].Length;
                    if (size > owner.Caps.MaxStorageBufferBindingSize)
                    {
                        throw new NotSupportedException("Raw storage allocation exceeds the device binding size.");
                    }

                    entries.Add(new() { binding = data.Binding(kind, index), buffer = (WGPUBufferImpl*)buffer, size = size });
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

    internal static WGPUPipelineLayoutImpl* CreateLayout(WgpuDevice owner, ShaderBindingData data, bool compute = false)
    {
        ulong visibility = (ulong)(compute ? A.ShaderStage.Compute : A.ShaderStage.Vertex | A.ShaderStage.Fragment);
        var entries = new List<WGPUBindGroupLayoutEntry>
        {
            new() { binding = 8, visibility = visibility, buffer = new() { type = WGPUBufferBindingType.Uniform } },
        };
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

        foreach ((string kind, IReadOnlyDictionary<object, int> resources) in new[] { ("buffer", data.Buffers), ("writable", data.Writable) })
        {
            foreach ((object _, int index) in resources)
            {
                entries.Add(new() { binding = data.Binding(kind, index), visibility = kind == "writable" && !compute ? (ulong)A.ShaderStage.Fragment : visibility, buffer = new() { type = kind == "buffer" ? WGPUBufferBindingType.ReadOnlyStorage : WGPUBufferBindingType.Storage } });
            }
        }

        if ((uint)entries.Count > owner.Limits.maxBindingsPerBindGroup)
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
