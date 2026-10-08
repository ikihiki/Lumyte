using Ahjo.Wgpu.Native;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class GraphicsPipeline : GpuResource
{
    internal GraphicsPipeline(WgpuDevice owner, A.RenderPipeline native, MaterialSchema? schema)
        : base(owner)
    {
        (Native, MaterialSchema) = (native, schema);
    }

    internal A.RenderPipeline Native { get; }

    internal MaterialSchema? MaterialSchema { get; }

    internal unsafe MaterialArguments CreateArguments(MaterialRegion region)
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            region.Check(Owner);
            if (MaterialSchema is null || !ReferenceEquals(MaterialSchema, region.Bindings.Layout.Handle))
            {
                throw new ArgumentException("Material layout belongs to another shader program.");
            }

            WGPULimits limits = Owner.Native.GetLimits();
            if (!region.Buffer.Usage.HasFlag(BufferUsage.ShaderRead) || region.Buffer.Usage.HasFlag(BufferUsage.ShaderWrite) || region.Offset % limits.minStorageBufferOffsetAlignment != 0 || region.Length > limits.maxStorageBufferBindingSize)
            {
                throw new ArgumentException("Material storage range or usage does not satisfy device limits.");
            }

            WGPUBindGroupLayoutImpl* layout = WGPU.wgpuRenderPipelineGetBindGroupLayout(Native.Handle, 0);
            try
            {
                WGPUBindGroupEntry* entries = stackalloc WGPUBindGroupEntry[9];
                entries[0] = new()
                {
                    binding = 0,
                    buffer = region.Buffer.Native.Handle,
                    offset = region.Offset,
                    size = region.Length,
                };
                for (int i = 0; i < 4; i++)
                {
                    SampledPair pair = region.Bindings.Pairs[i];
                    entries[1 + (i * 2)] = new()
                    {
                        binding = (uint)(1 + (i * 2)),
                        textureView = pair.View.Native.Handle,
                    };
                    entries[2 + (i * 2)] = new()
                    {
                        binding = (uint)(2 + (i * 2)),
                        sampler = pair.Sampler.Handle,
                    };
                }

                var desc = new WGPUBindGroupDescriptor
                {
                    label = new()
                    {
                        length = nuint.MaxValue,
                    },
                    layout = layout,
                    entryCount = 9,
                    entries = entries,
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
                    throw new InvalidOperationException("Material bind group creation failed.");
                }

                return new(this, region, handle);
            }
            finally
            {
                WGPU.wgpuBindGroupLayoutRelease(layout);
            }
        }
    }

    protected override void ReleaseNative() => Native.Dispose();
}
