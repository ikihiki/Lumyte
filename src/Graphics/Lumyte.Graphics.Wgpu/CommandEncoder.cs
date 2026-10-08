using Ahjo.Wgpu.Native;

namespace Lumyte.Graphics.Wgpu;

internal sealed unsafe class CommandEncoder : IDisposable
{
    private WGPUCommandEncoderImpl* _handle;
    private HashSet<GpuResource> _resources = [];
    private List<MaterialTransfer> _materialTransfers = [];
    private RenderEncoder? _render;
    private bool _finished;

    internal CommandEncoder(WgpuDevice owner, WGPUCommandEncoderImpl* handle)
    {
        Owner = owner;
        _handle = handle;
        owner.EncoderCount++;
    }

    internal WgpuDevice Owner { get; }

    public RenderEncoder BeginRenderPass(RenderPassDesc desc)
    {
        lock (Owner.Gate)
        {
            OutsidePass();
            ArgumentNullException.ThrowIfNull(desc);
            ArgumentNullException.ThrowIfNull(desc.Target);
            desc.Target.Check(Owner);
            if (!Enum.IsDefined(desc.Load) || !Enum.IsDefined(desc.Store))
            {
                throw new ArgumentException("Invalid load/store operation.");
            }

            if (!desc.Target.Texture.Usage.HasFlag(TextureUsage.RenderAttachment) || desc.Target.Texture.Format != TextureFormat.Rgba8Unorm)
            {
                throw new ArgumentException("Initial targets require RGBA8Unorm RenderAttachment usage.");
            }

            Color4 c = desc.ClearValue;
            if (!double.IsFinite(c.R) || !double.IsFinite(c.G) || !double.IsFinite(c.B) || !double.IsFinite(c.A))
            {
                throw new ArgumentException("Clear value must be finite.");
            }

            var attachment = new WGPURenderPassColorAttachment
            {
                view = desc.Target.Native.Handle,
                depthSlice = uint.MaxValue,
                loadOp = desc.Load == LoadOp.Clear ? WGPULoadOp.Clear : WGPULoadOp.Load,
                storeOp = desc.Store == StoreOp.Store ? WGPUStoreOp.Store : WGPUStoreOp.Discard,
                clearValue = new WGPUColor
                {
                    r = c.R,
                    g = c.G,
                    b = c.B,
                    a = c.A,
                },
            };
            var native = new WGPURenderPassDescriptor
            {
                label = new WGPUStringView
                {
                    length = nuint.MaxValue,
                },
                colorAttachmentCount = 1,
                colorAttachments = &attachment,
            };
            WGPURenderPassEncoderImpl* handle = WGPU.wgpuCommandEncoderBeginRenderPass(_handle, &native);
            Use(desc.Target);
            _render = new(this, handle, desc.Target.Texture);
            return _render;
        }
    }

    public void Dispatch(ComputePipeline pipeline, ShaderArguments arguments, uint x, uint y = 1, uint z = 1)
    {
        lock (Owner.Gate)
        {
            OutsidePass();
            pipeline.Check(Owner);
            arguments.Check(Owner);
            if (!ReferenceEquals(arguments.Pipeline, pipeline))
            {
                throw new ArgumentException("Arguments belong to another pipeline.");
            }

            uint max = Owner.Native.GetLimits().maxComputeWorkgroupsPerDimension;
            if (x > max || y > max || z > max)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            Use(pipeline);
            Use(arguments);
            WGPUComputePassEncoderImpl* pass = WGPU.wgpuCommandEncoderBeginComputePass(_handle, null);
            try
            {
                WGPU.wgpuComputePassEncoderSetPipeline(pass, pipeline.Native.Handle);
                WGPU.wgpuComputePassEncoderSetBindGroup(pass, 0, arguments.Handle, 0, null);
                WGPU.wgpuComputePassEncoderDispatchWorkgroups(pass, x, y, z);
                WGPU.wgpuComputePassEncoderEnd(pass);
            }
            finally
            {
                WGPU.wgpuComputePassEncoderRelease(pass);
            }
        }
    }

    public void RecordCopyBuffer(BufferSlice source, BufferSlice destination)
    {
        lock (Owner.Gate)
        {
            OutsidePass();
            if (source.Buffer is null || destination.Buffer is null)
            {
                throw new ArgumentException("Invalid buffer slice.");
            }

            source.Buffer.Check(Owner);
            destination.Buffer.Check(Owner);
            if (ReferenceEquals(source.Buffer, destination.Buffer) || source.Length != destination.Length || source.Offset % WgpuDevice.CopyOffsetAlignmentInBytes != 0 || destination.Offset % WgpuDevice.CopyOffsetAlignmentInBytes != 0 || source.Length % WgpuDevice.CopySizeAlignmentInBytes != 0 || !source.Buffer.Usage.HasFlag(BufferUsage.CopySource) || !destination.Buffer.Usage.HasFlag(BufferUsage.CopyDestination))
            {
                throw new ArgumentException("Copy requires distinct buffers, matching aligned ranges and copy usages.");
            }

            MaterialRegion? material = source.Buffer.FindMaterial(source.Offset, source.Length);
            destination.Buffer.InvalidateMaterials(destination.Offset, destination.Length);
            _materialTransfers.RemoveAll(t => ReferenceEquals(t.Region.Buffer, destination.Buffer) && t.Region.Offset < destination.Offset + destination.Length && destination.Offset < t.Region.Offset + t.Region.Length);
            if (material is not null)
            {
                Use(material.Bindings);
                _materialTransfers.Add(new(destination.Buffer.RegisterMaterial(destination.Offset, destination.Length, material.Bindings, ready: false)));
            }

            Use(source.Buffer);
            Use(destination.Buffer);
            WGPU.wgpuCommandEncoderCopyBufferToBuffer(_handle, source.Buffer.Native.Handle, source.Offset, destination.Buffer.Native.Handle, destination.Offset, source.Length);
        }
    }

    public void RecordCopyBufferToTexture(BufferSlice source, Texture destination, uint bytesPerRow)
    {
        lock (Owner.Gate)
        {
            OutsidePass();
            source.Buffer.Check(Owner);
            destination.Check(Owner);
            ulong required = checked(((ulong)bytesPerRow * (destination.Height - 1)) + ((ulong)destination.Width * 4));
            if (!source.Buffer.Usage.HasFlag(BufferUsage.CopySource) || !destination.Usage.HasFlag(TextureUsage.CopyDestination) || source.Offset % 4 != 0 || bytesPerRow % 256 != 0 || bytesPerRow < (ulong)destination.Width * 4 || required > source.Length)
            {
                throw new ArgumentException("Texture upload requires copy usages, aligned pitch and sufficient source range.");
            }

            Use(source.Buffer);
            Use(destination);
            var src = new WGPUTexelCopyBufferInfo
            {
                buffer = source.Buffer.Native.Handle,
                layout = new WGPUTexelCopyBufferLayout
                {
                    offset = source.Offset,
                    bytesPerRow = bytesPerRow,
                    rowsPerImage = destination.Height,
                },
            };
            var dst = new WGPUTexelCopyTextureInfo
            {
                texture = destination.Native.Handle,
                aspect = WGPUTextureAspect.All,
            };
            var extent = new WGPUExtent3D
            {
                width = destination.Width,
                height = destination.Height,
                depthOrArrayLayers = 1,
            };
            WGPU.wgpuCommandEncoderCopyBufferToTexture(_handle, &src, &dst, &extent);
        }
    }

    public void RecordCopyTextureToBuffer(Texture source, WgpuBuffer destination, uint bytesPerRow)
    {
        lock (Owner.Gate)
        {
            OutsidePass();
            source.Check(Owner);
            destination.Check(Owner);
            ulong required = checked(((ulong)bytesPerRow * (source.Height - 1)) + ((ulong)source.Width * 4));
            if (!source.Usage.HasFlag(TextureUsage.CopySource) || bytesPerRow % 256 != 0 || bytesPerRow < (ulong)source.Width * 4 || required > destination.SizeInBytes || !destination.Usage.HasFlag(BufferUsage.CopyDestination))
            {
                throw new ArgumentException("Texture copy requires aligned rows and sufficient CopyDestination storage.");
            }

            destination.InvalidateMaterials(0, required);
            _materialTransfers.RemoveAll(t => ReferenceEquals(t.Region.Buffer, destination) && t.Region.Offset < required);
            Use(source);
            Use(destination);
            var src = new WGPUTexelCopyTextureInfo
            {
                texture = source.Native.Handle,
                aspect = WGPUTextureAspect.All,
            };
            var dst = new WGPUTexelCopyBufferInfo
            {
                buffer = destination.Native.Handle,
                layout = new WGPUTexelCopyBufferLayout
                {
                    bytesPerRow = bytesPerRow,
                    rowsPerImage = source.Height,
                },
            };
            var extent = new WGPUExtent3D
            {
                width = source.Width,
                height = source.Height,
                depthOrArrayLayers = 1,
            };
            WGPU.wgpuCommandEncoderCopyTextureToBuffer(_handle, &src, &dst, &extent);
        }
    }

    public CommandBuffer Finish()
    {
        lock (Owner.Gate)
        {
            OutsidePass();
            WGPUCommandBufferImpl* handle = WGPU.wgpuCommandEncoderFinish(_handle, null);
            if (handle == null)
            {
                throw new InvalidOperationException("Command buffer creation failed.");
            }

            _finished = true;
            WGPU.wgpuCommandEncoderRelease(_handle);
            _handle = null;
            Owner.EncoderCount--;
            HashSet<GpuResource> resources = _resources;
            _resources = [];
            List<MaterialTransfer> transfers = _materialTransfers;
            _materialTransfers = [];
            return new(Owner, handle, resources, transfers);
        }
    }

    public void Dispose()
    {
        lock (Owner.Gate)
        {
            if (_handle == null)
            {
                return;
            }

            _render?.Abort();
            _render = null;
            WGPU.wgpuCommandEncoderRelease(_handle);
            _handle = null;
            Owner.EncoderCount--;
            foreach (GpuResource resource in _resources)
            {
                resource.ReleaseLease();
            }

            _resources.Clear();
            foreach (MaterialTransfer t in _materialTransfers)
            {
                t.Region.Buffer.CancelMaterial(t.Region);
            }

            _materialTransfers.Clear();
        }
    }

    internal void Active()
    {
        Owner.Check();
        if (_handle == null || _finished)
        {
            throw new InvalidOperationException("Encoder is no longer recording.");
        }
    }

    internal void Use(GpuResource resource)
    {
        resource.Check(Owner);
        if (_resources.Add(resource))
        {
            resource.Acquire();
        }
    }

    internal void EndPass(RenderEncoder render)
    {
        if (!ReferenceEquals(_render, render))
        {
            throw new InvalidOperationException("Pass is not active.");
        }

        _render = null;
    }

    private void OutsidePass()
    {
        Active();
        if (_render is not null)
        {
            throw new InvalidOperationException("End the render pass before this operation.");
        }
    }
}
