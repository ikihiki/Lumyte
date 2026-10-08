using Ahjo.Wgpu.Native;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class ComputePipeline : GpuResource
{
    internal ComputePipeline(WgpuDevice owner, A.ComputePipeline native)
        : base(owner)
    {
        Native = native;
    }

    internal A.ComputePipeline Native { get; }

    public unsafe ShaderArguments CreateArguments(GpuReference<uint> data)
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            BufferSlice slice = data.Data;
            if (slice.Buffer is null)
            {
                throw new ArgumentException("Reference was not created by a device.");
            }

            slice.Buffer.Check(Owner);
            if (!slice.Buffer.Usage.HasFlag(BufferUsage.ShaderWrite))
            {
                throw new ArgumentException("ShaderWrite usage is required.");
            }

            WGPULimits limits = Owner.Native.GetLimits();
            if (slice.Offset % limits.minStorageBufferOffsetAlignment != 0 || slice.Length > limits.maxStorageBufferBindingSize)
            {
                throw new ArgumentException("Storage buffer reference does not satisfy device limits.");
            }

            // The first implementation has one logical RWStructuredBuffer<uint> argument.
            // Its binding is library-owned; callers pass only the opaque data reference.
            WGPUBindGroupLayoutImpl* layout = WGPU.wgpuComputePipelineGetBindGroupLayout(Native.Handle, 0);
            try
            {
                var entry = new WGPUBindGroupEntry
                {
                    binding = 0,
                    buffer = slice.Buffer.Native.Handle,
                    offset = slice.Offset,
                    size = slice.Length,
                };
                var desc = new WGPUBindGroupDescriptor
                {
                    label = new WGPUStringView
                    {
                        length = nuint.MaxValue,
                    },
                    layout = layout,
                    entryCount = 1,
                    entries = &entry,
                };
                WGPUBindGroupImpl* handle = WGPU.wgpuDeviceCreateBindGroup(Owner.Native.Handle, &desc);
                try
                {
                    Owner.CheckErrors();
                }
                catch
                {
                    if (handle != null)
                    {
                        WGPU.wgpuBindGroupRelease(handle);
                    }

                    throw;
                }

                if (handle == null)
                {
                    throw new InvalidOperationException("Bind group creation failed.");
                }

                return new ShaderArguments(this, slice.Buffer, handle);
            }
            finally
            {
                WGPU.wgpuBindGroupLayoutRelease(layout);
            }
        }
    }

    protected override void ReleaseNative() => Native.Dispose();
}
