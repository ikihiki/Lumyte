using System.Text;
using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe partial class WgpuComputePipeline
{
    private WGPUComputePipelineImpl* _native;

    internal WGPUComputePipelineImpl* Native
    {
        get
        {
            ValidateAlive();
            return _native;
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

    private void DisposeNative() => WGPU.wgpuComputePipelineRelease(_native);
}
