using System.Buffers.Binary;
using Ahjo.Wgpu.Native;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal sealed class GraphicsPipeline : GpuResource
{
    private readonly Texture? _fallbackTexture;
    private readonly TextureView? _fallbackView;
    private readonly Sampler? _fallbackSampler;

    internal GraphicsPipeline(WgpuDevice owner, A.RenderPipeline native, ShaderDataSchema? schema)
        : base(owner)
    {
        (Native, ShaderDataSchema) = (native, schema);
        if (schema is not null)
        {
            try
            {
                _fallbackTexture = owner.CreateTexture(new TextureDesc { Width = 1, Height = 1, Usage = TextureUsage.Sampled });
                _fallbackView = (TextureView)_fallbackTexture.CreateView();
                _fallbackSampler = owner.CreateSampler(new SamplerDesc());
            }
            catch
            {
                _fallbackSampler?.Dispose();
                _fallbackView?.Dispose();
                _fallbackTexture?.Dispose();
                Native.Dispose();
                Owner.ResourceCount--;
                throw;
            }
        }
    }

    internal A.RenderPipeline Native { get; }

    internal ShaderDataSchema? ShaderDataSchema { get; }

    internal static unsafe A.PipelineLayout CreateLayout(WgpuDevice owner)
    {
        WGPULimits limits = owner.Native.GetLimits();
        if (limits.maxSampledTexturesPerShaderStage < 8 || limits.maxSamplersPerShaderStage < 4 || limits.maxStorageBuffersPerShaderStage < 5 || limits.maxUniformBuffersPerShaderStage < 1 || limits.maxBindingsPerBindGroup < 18)
        {
            throw new NotSupportedException("Bindless shader requires eight textures, four samplers, five storage buffers and one uniform buffer per stage.");
        }

        var entries = new A.BindGroupLayoutEntry[18];
        for (int i = 0; i < entries.Length; i++)
        {
            entries[i].Binding = (uint)i;
            entries[i].Visibility = A.ShaderStage.Vertex | A.ShaderStage.Fragment;
            if (i == 0 || i == 1 || i >= 14)
            {
                entries[i].Type = A.BindGroupLayoutEntry.Kind.Buffer;
                entries[i].BufferType = i == 1 ? WGPUBufferBindingType.Uniform : WGPUBufferBindingType.ReadOnlyStorage;
            }
            else if (i < 10)
            {
                entries[i].Type = A.BindGroupLayoutEntry.Kind.Texture;
                entries[i].TextureSampleType = WGPUTextureSampleType.Float;
                entries[i].TextureViewDimension = WGPUTextureViewDimension._2D;
            }
            else
            {
                entries[i].Type = A.BindGroupLayoutEntry.Kind.Sampler;
                entries[i].SamplerType = WGPUSamplerBindingType.Filtering;
            }
        }

        using A.BindGroupLayout group = owner.Native.CreateBindGroupLayout(entries);
        return owner.Native.CreatePipelineLayout([group]);
    }

    internal unsafe DrawingArguments CreateArguments(ShaderDataRegion region, DescriptorRegistration registration)
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            region.Check(Owner);
            if (ShaderDataSchema is null || !ReferenceEquals(ShaderDataSchema, region.Snapshot.Schema))
            {
                throw new ArgumentException("Shader data layout belongs to another shader program.");
            }

            int count = checked((int)(region.Length / ShaderDataSchema.ElementStrideInBytes));
            DescriptorRegistration[] dependencies = region.Snapshot.Elements.Skip(region.FirstElement).Take(count).SelectMany(element => element).Distinct().ToArray();
            foreach (DescriptorRegistration dependency in dependencies)
            {
                dependency.Check(Owner);
                dependency.Resource.Check(Owner);
            }

            DescriptorRegistration[] textures = dependencies.Where(entry => entry.Resource is TextureView).DistinctBy(entry => entry.Identity).OrderBy(entry => entry.Identity).ToArray();
            DescriptorRegistration[] samplers = dependencies.Where(entry => entry.Resource is Sampler).DistinctBy(entry => entry.Identity).OrderBy(entry => entry.Identity).ToArray();
            DescriptorRegistration[] buffers = dependencies.Where(entry => entry.Resource is WgpuBuffer).DistinctBy(entry => entry.Identity).OrderBy(entry => entry.Identity).ToArray();
            if (textures.Length > 8 || samplers.Length > 4 || buffers.Length > 4)
            {
                throw new NotSupportedException($"Reachable resources exceed compiled capacities: textures {textures.Length}/8, samplers {samplers.Length}/4, buffers {buffers.Length}/4. Draws are not split automatically.");
            }

            WGPULimits limits = Owner.Native.GetLimits();
            if (!region.Buffer.Usage.HasFlag(BufferUsage.ShaderRead) || region.Buffer.Usage.HasFlag(BufferUsage.ShaderWrite) || region.Buffer.SizeInBytes > limits.maxStorageBufferBindingSize)
            {
                throw new ArgumentException("Shader data storage range does not satisfy device limits.");
            }

            // Bind the unchanged enclosing allocation; logical element offsets are carried in the root table.
            // No user buffer, element count or byte size is padded or rewritten.
            if (region.Offset % ShaderDataSchema.ElementStrideInBytes != 0)
            {
                throw new ArgumentException("Shader data root is not element aligned.");
            }

            byte[] lookupBytes = new byte[272];
            WriteWord(lookupBytes, 0, checked((uint)(region.Offset / ShaderDataSchema.ElementStrideInBytes)));
            WriteWord(lookupBytes, 4, (uint)count);
            for (int i = 0; i < textures.Length; i++)
            {
                WriteWord(lookupBytes, 16 + (i * 16), textures[i].Identity);
            }

            for (int i = 0; i < samplers.Length; i++)
            {
                WriteWord(lookupBytes, 144 + (i * 16), samplers[i].Identity);
            }

            for (int i = 0; i < buffers.Length; i++)
            {
                if (buffers[i].Range.Buffer.SizeInBytes > limits.maxStorageBufferBindingSize)
                {
                    throw new ArgumentException("Referenced buffer exceeds storage binding size.");
                }

                WriteWord(lookupBytes, 208 + (i * 16), buffers[i].Identity);
                WriteWord(lookupBytes, 212 + (i * 16), checked((uint)buffers[i].Range.Offset));
                WriteWord(lookupBytes, 216 + (i * 16), checked((uint)buffers[i].Range.Length));
            }

            A.Buffer lookup = Owner.Native.CreateBuffer(new A.BufferDescriptor { Size = 272, Usage = A.BufferUsage.Uniform, MappedAtCreation = true });
            WGPUBindGroupImpl* handle = null;
            try
            {
                lookupBytes.CopyTo(lookup.GetMappedRange<byte>(0, 272));
                lookup.Unmap();
                WGPUBindGroupEntry* entries = stackalloc WGPUBindGroupEntry[18];
                entries[0] = new() { binding = 0, buffer = region.Buffer.Native.Handle, size = region.Buffer.SizeInBytes };
                entries[1] = new() { binding = 1, buffer = lookup.Handle, size = 272 };
                for (int i = 0; i < 8; i++)
                {
                    TextureView view = textures.Length == 0 ? _fallbackView! : (TextureView)textures[Math.Min(i, textures.Length - 1)].Resource;
                    entries[2 + i] = new() { binding = (uint)(2 + i), textureView = view.Native.Handle };
                }

                for (int i = 0; i < 4; i++)
                {
                    Sampler sampler = samplers.Length == 0 ? _fallbackSampler! : (Sampler)samplers[Math.Min(i, samplers.Length - 1)].Resource;
                    entries[10 + i] = new() { binding = (uint)(10 + i), sampler = sampler.Handle };
                    WgpuBuffer buffer = buffers.Length == 0 ? region.Buffer : buffers[Math.Min(i, buffers.Length - 1)].Range.Buffer;
                    entries[14 + i] = new() { binding = (uint)(14 + i), buffer = buffer.Native.Handle, size = buffer.SizeInBytes };
                }

                WGPUBindGroupLayoutImpl* layout = WGPU.wgpuRenderPipelineGetBindGroupLayout(Native.Handle, 0);
                try
                {
                    var desc = new WGPUBindGroupDescriptor { label = new() { length = nuint.MaxValue }, layout = layout, entryCount = 18, entries = entries };
                    handle = WGPU.wgpuDeviceCreateBindGroup(Owner.Native.Handle, &desc);
                    Owner.CheckErrors();
                    if (handle == null)
                    {
                        throw new InvalidOperationException("Automatic drawing bind group creation failed.");
                    }
                }
                finally
                {
                    WGPU.wgpuBindGroupLayoutRelease(layout);
                }

                return new(this, region, dependencies, lookup, handle, registration);
            }
            catch
            {
                if (handle != null)
                {
                    WGPU.wgpuBindGroupRelease(handle);
                }

                lookup.Dispose();
                throw;
            }
        }
    }

    protected override void ReleaseNative()
    {
        Native.Dispose();
        _fallbackSampler?.Dispose();
        _fallbackView?.Dispose();
        _fallbackTexture?.Dispose();
    }

    private static void WriteWord(byte[] destination, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(destination.AsSpan(offset, 4), value);
}
