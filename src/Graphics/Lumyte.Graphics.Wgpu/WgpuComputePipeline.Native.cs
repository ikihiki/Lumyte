using System.Text;
using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe partial class WgpuComputePipeline
{
    private readonly Dictionary<string, nint> _variants = [];
    private WGPUComputePipelineImpl* _native;

    internal WGPUComputePipelineImpl* Native
    {
        get
        {
            ValidateAlive();
            return _native;
        }
    }

    internal WGPUComputePipelineImpl* Resolve(ShaderBindingData? arguments)
    {
        if (arguments == null)
        {
            return Native;
        }

        ValidateAlive();
        if (_variants.TryGetValue(arguments.Key, out nint cached))
        {
            return (WGPUComputePipelineImpl*)cached;
        }

        using Ahjo.Wgpu.ShaderModule module = _owner.NativeDevice.CreateShaderModule(new Ahjo.Wgpu.ShaderModuleDescriptor { Source = Ahjo.Wgpu.ShaderSource.FromWgsl(Encoding.UTF8.GetBytes(arguments.Specialize(Data))) });
        WGPUPipelineLayoutImpl* layout = WgpuShaderBinding.CreateLayout(_owner, arguments, true);
        try
        {
            byte[] entry = Encoding.UTF8.GetBytes(Data.EntryPoint);
            fixed (byte* bytes = entry)
            {
                var desc = new WGPUComputePipelineDescriptor { layout = layout, compute = new() { module = module.Handle, entryPoint = new() { data = (sbyte*)bytes, length = (nuint)entry.Length } } };
                WGPUComputePipelineImpl* pipeline = WGPU.wgpuDeviceCreateComputePipeline(_owner.NativeDevice.Handle, &desc);
                if (pipeline == null)
                {
                    throw new InvalidOperationException("WebGPU compute pipeline specialization failed.");
                }

                _variants.Add(arguments.Key, (nint)pipeline);
                return pipeline;
            }
        }
        finally
        {
            WGPU.wgpuPipelineLayoutRelease(layout);
        }
    }

    private void Initialize()
    {
        byte[] entry = Encoding.UTF8.GetBytes(Data.EntryPoint);
        fixed (byte* bytes = entry)
        {
            var desc = new WGPUComputePipelineDescriptor
            {
                compute = new() { module = _compute.Native.Handle, entryPoint = new() { data = (sbyte*)bytes, length = (nuint)entry.Length } },
            };
            _native = WGPU.wgpuDeviceCreateComputePipeline(_owner.NativeDevice.Handle, &desc);
            if (_native == null)
            {
                throw new InvalidOperationException("WebGPU compute pipeline creation failed.");
            }
        }
    }

    private void DisposeNative()
    {
        WGPU.wgpuComputePipelineRelease(_native);
        foreach (nint pipeline in _variants.Values)
        {
            WGPU.wgpuComputePipelineRelease((WGPUComputePipelineImpl*)pipeline);
        }

        _variants.Clear();
    }
}
