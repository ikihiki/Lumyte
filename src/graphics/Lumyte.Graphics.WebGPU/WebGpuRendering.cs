using System.Text;

using Silk.NET.WebGPU;

using WgpuBuffer = Silk.NET.WebGPU.Buffer;

namespace Lumyte.Graphics.WebGPU;

public sealed unsafe partial class WebGpuDevice
{
    public void WriteTexture(
        GpuTextureHandle texture,
        ReadOnlySpan<byte> source,
        GpuTextureCopyFootprint footprint)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        footprint.Validate();
        if (!textures.TryGetValue(texture.Value, out TextureRecord? record))
        {
            throw new ArgumentException("Texture does not belong to this WebGPU device.", nameof(texture));
        }
        if (footprint.Width > record.Description.Width
            || footprint.Height > record.Description.Height
            || footprint.BytesPerPixel != GpuBackendCommands.BytesPerPixel(record.Description.Format)
            || checked((ulong)source.Length) != footprint.RequiredBytes)
        {
            throw new ArgumentException("Source and footprint are incompatible with the texture.", nameof(footprint));
        }

        var destination = new ImageCopyTexture
        {
            Texture = (Texture*)record.Handle,
            Aspect = TextureAspect.All,
        };
        var layout = new TextureDataLayout
        {
            BytesPerRow = checked((uint)footprint.RowPitch),
            RowsPerImage = footprint.Height,
        };
        var extent = new Extent3D(footprint.Width, footprint.Height, 1);
        fixed (byte* bytes = source)
        {
            api.QueueWriteTexture(queue, in destination, bytes, checked((nuint)source.Length), in layout, in extent);
        }
    }

    public byte[] ReadTexture(GpuTextureHandle texture, GpuTextureCopyFootprint footprint)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        footprint.Validate();
        if (!textures.TryGetValue(texture.Value, out TextureRecord? record))
        {
            throw new ArgumentException("Texture does not belong to this WebGPU device.", nameof(texture));
        }
        if (footprint.Width > record.Description.Width
            || footprint.Height > record.Description.Height
            || footprint.BytesPerPixel != GpuBackendCommands.BytesPerPixel(record.Description.Format))
        {
            throw new ArgumentException("Footprint is incompatible with the texture.", nameof(footprint));
        }

        uint tightRowPitch = checked(footprint.Width * footprint.BytesPerPixel);
        uint nativeRowPitch = Align(tightRowPitch, 256);
        ulong nativeSize = checked((ulong)nativeRowPitch * footprint.Height);
        WgpuBuffer* readback = CreateNativeBuffer(nativeSize, BufferUsage.CopyDst | BufferUsage.MapRead);
        try
        {
            var source = new ImageCopyTexture
            {
                Texture = (Texture*)record.Handle,
                Aspect = TextureAspect.All,
            };
            var destination = new ImageCopyBuffer
            {
                Buffer = readback,
                Layout = new TextureDataLayout
                {
                    BytesPerRow = nativeRowPitch,
                    RowsPerImage = footprint.Height,
                },
            };
            var extent = new Extent3D(footprint.Width, footprint.Height, 1);
            SubmitTextureReadback(source, destination, extent);
            byte[] padded = MapReadback(readback, checked((nuint)nativeSize));
            byte[] result = new byte[checked((int)footprint.RequiredBytes)];
            for (uint row = 0; row < footprint.Height; row++)
            {
                padded.AsSpan(checked((int)(row * nativeRowPitch)), checked((int)tightRowPitch))
                    .CopyTo(result.AsSpan(checked((int)(row * footprint.RowPitch))));
            }
            return result;
        }
        finally
        {
            api.BufferRelease(readback);
        }
    }

    internal byte[] RenderOffscreen(
        string shaderSource,
        string vertexEntryPoint,
        string fragmentEntryPoint,
        uint vertexCount,
        uint width,
        uint height,
        GpuResourceTable? resources = null)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderSource);
        ArgumentException.ThrowIfNullOrWhiteSpace(vertexEntryPoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(fragmentEntryPoint);
        if (vertexCount == 0) { throw new ArgumentOutOfRangeException(nameof(vertexCount)); }
        if (width == 0 || height == 0) { throw new ArgumentOutOfRangeException(nameof(width)); }

        nint pipelineHandle = 0;
        nint layoutHandle = 0;
        Texture* target = null;
        TextureView* targetView = null;
        WgpuBuffer* readback = null;
        CommandEncoder* encoder = null;
        RenderPassEncoder* pass = null;
        CommandBuffer* commands = null;
        try
        {
            pipelineHandle = CreateNativeRasterPipeline(
                shaderSource,
                vertexEntryPoint,
                fragmentEntryPoint,
                new GpuRasterPipelineDescription([new(GpuFormat.Rgba8Unorm)]));
            var pipeline = (RenderPipeline*)pipelineHandle;
            BindGroup* bindGroup = null;
            if (resources is not null)
            {
                layoutHandle = GetNativeBindGroupLayout(pipelineHandle);
                bindGroup = (BindGroup*)GetOrCreateBindGroup(resources, layoutHandle);
            }

            target = CreateNativeTexture(width, height, TextureUsage.RenderAttachment | TextureUsage.CopySrc);
            targetView = api.TextureCreateView(target, null);
            if (targetView is null) { throw new InvalidOperationException("WebGPU target view creation failed."); }
            uint rowPitch = Align(checked(width * 4), 256);
            readback = CreateNativeBuffer(checked((ulong)rowPitch * height), BufferUsage.CopyDst | BufferUsage.MapRead);
            var encoderDescription = new CommandEncoderDescriptor();
            encoder = api.DeviceCreateCommandEncoder(device, in encoderDescription);
            if (encoder is null) { throw new InvalidOperationException("WebGPU command encoder creation failed."); }

            var colorAttachment = new RenderPassColorAttachment
            {
                View = targetView,
                DepthSlice = uint.MaxValue,
                LoadOp = LoadOp.Clear,
                StoreOp = StoreOp.Store,
                ClearValue = new Color(0, 0, 0, 1),
            };
            var passDescription = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment,
            };
            pass = api.CommandEncoderBeginRenderPass(encoder, in passDescription);
            if (pass is null) { throw new InvalidOperationException("WebGPU render pass creation failed."); }
            api.RenderPassEncoderSetPipeline(pass, pipeline);
            if (bindGroup is not null) { api.RenderPassEncoderSetBindGroup(pass, 0, bindGroup, 0, null); }
            api.RenderPassEncoderSetViewport(pass, 0, 0, width, height, 0, 1);
            api.RenderPassEncoderSetScissorRect(pass, 0, 0, width, height);
            api.RenderPassEncoderDraw(pass, vertexCount, 1, 0, 0);
            api.RenderPassEncoderEnd(pass);
            api.RenderPassEncoderRelease(pass);
            pass = null;

            var textureCopy = new ImageCopyTexture { Texture = target, Aspect = TextureAspect.All };
            var bufferCopy = new ImageCopyBuffer
            {
                Buffer = readback,
                Layout = new TextureDataLayout { BytesPerRow = rowPitch, RowsPerImage = height },
            };
            var extent = new Extent3D(width, height, 1);
            api.CommandEncoderCopyTextureToBuffer(encoder, in textureCopy, in bufferCopy, in extent);
            var commandDescription = new CommandBufferDescriptor();
            commands = api.CommandEncoderFinish(encoder, in commandDescription);
            if (commands is null) { throw new InvalidOperationException("WebGPU command buffer creation failed."); }
            api.QueueSubmit(queue, 1, ref commands);

            byte[] padded = MapReadback(readback, checked((nuint)((ulong)rowPitch * height)));
            uint tightRowPitch = checked(width * 4);
            if (rowPitch == tightRowPitch) { return padded; }
            byte[] result = new byte[checked((int)((ulong)tightRowPitch * height))];
            for (uint row = 0; row < height; row++)
            {
                padded.AsSpan(checked((int)(row * rowPitch)), checked((int)tightRowPitch))
                    .CopyTo(result.AsSpan(checked((int)(row * tightRowPitch))));
            }
            return result;
        }
        finally
        {
            if (pass is not null) { api.RenderPassEncoderRelease(pass); }
            if (commands is not null) { api.CommandBufferRelease(commands); }
            if (encoder is not null) { api.CommandEncoderRelease(encoder); }
            if (readback is not null) { api.BufferRelease(readback); }
            if (targetView is not null) { api.TextureViewRelease(targetView); }
            if (target is not null) { api.TextureRelease(target); }
            if (layoutHandle != 0) { ReleaseNativeBindGroupLayout(layoutHandle); }
            if (pipelineHandle != 0) { ReleaseNativeRasterPipeline(pipelineHandle); }
        }
    }

    internal nint CreateNativeRasterPipeline(
        string shaderSource,
        string vertexEntryPoint,
        string fragmentEntryPoint,
        GpuRasterPipelineDescription pipelineDescription)
    {
        ShaderModule* shader = CreateNativeShaderModule(shaderSource);
        try
        {
            byte[] vertexEntryBytes = Encoding.UTF8.GetBytes(vertexEntryPoint + '\0');
            byte[] fragmentEntryBytes = Encoding.UTF8.GetBytes(fragmentEntryPoint + '\0');
            fixed (byte* vertexEntry = vertexEntryBytes)
            fixed (byte* fragmentEntry = fragmentEntryBytes)
            {
                GpuColorTargetDescription colorDescription = pipelineDescription.ColorTargets[0];
                GpuBlendDescription? blendDescription = pipelineDescription.EmbeddedBlend;
                var blend = new Silk.NET.WebGPU.BlendState
                {
                    Color = ToWebGpuBlendComponent(blendDescription, alpha: false),
                    Alpha = ToWebGpuBlendComponent(blendDescription, alpha: true),
                };
                var target = new ColorTargetState
                {
                    Format = ToWebGpuFormat(colorDescription.Format),
                    WriteMask = ToWebGpuColorWriteMask(
                        colorDescription.WriteMask & (blendDescription?.ColorWriteMask ?? GpuColorWriteMask.All)),
                    Blend = blendDescription is null ? null : &blend,
                };
                var fragment = new FragmentState
                {
                    Module = shader,
                    EntryPoint = fragmentEntry,
                    TargetCount = 1,
                    Targets = &target,
                };
                var depthStencil = new Silk.NET.WebGPU.DepthStencilState
                {
                    Format = pipelineDescription.DepthFormat is { } depthFormat
                        ? ToWebGpuFormat(depthFormat)
                        : pipelineDescription.StencilFormat is { } stencilFormat
                            ? ToWebGpuFormat(stencilFormat)
                            : TextureFormat.Undefined,
                    DepthWriteEnabled = pipelineDescription.DepthFormat is not null,
                    DepthCompare = pipelineDescription.DepthFormat is not null
                        ? CompareFunction.LessEqual
                        : CompareFunction.Always,
                    StencilFront = new StencilFaceState
                    {
                        Compare = CompareFunction.Always,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.Keep,
                    },
                    StencilBack = new StencilFaceState
                    {
                        Compare = CompareFunction.Always,
                        FailOp = StencilOperation.Keep,
                        DepthFailOp = StencilOperation.Keep,
                        PassOp = StencilOperation.Keep,
                    },
                    StencilReadMask = byte.MaxValue,
                    StencilWriteMask = byte.MaxValue,
                };
                var description = new RenderPipelineDescriptor
                {
                    Vertex = new VertexState { Module = shader, EntryPoint = vertexEntry },
                    Primitive = new PrimitiveState
                    {
                        Topology = pipelineDescription.Topology switch
                        {
                            GpuPrimitiveTopology.TriangleList => PrimitiveTopology.TriangleList,
                            GpuPrimitiveTopology.TriangleStrip => PrimitiveTopology.TriangleStrip,
                            _ => throw new ArgumentOutOfRangeException(nameof(pipelineDescription)),
                        },
                        FrontFace = pipelineDescription.FrontFace == GpuFrontFace.CounterClockwise
                            ? FrontFace.Ccw
                            : FrontFace.CW,
                        CullMode = pipelineDescription.CullMode switch
                        {
                            GpuCullMode.None => CullMode.None,
                            GpuCullMode.Front => CullMode.Front,
                            GpuCullMode.Back => CullMode.Back,
                            _ => throw new ArgumentOutOfRangeException(nameof(pipelineDescription)),
                        },
                    },
                    Multisample = new MultisampleState
                    {
                        Count = pipelineDescription.SampleCount,
                        Mask = uint.MaxValue,
                        AlphaToCoverageEnabled = pipelineDescription.AlphaToCoverage,
                    },
                    Fragment = &fragment,
                    DepthStencil = pipelineDescription.DepthFormat is null && pipelineDescription.StencilFormat is null
                        ? null
                        : &depthStencil,
                };
                RenderPipeline* pipeline = api.DeviceCreateRenderPipeline(device, in description);
                return pipeline is null
                    ? throw new InvalidOperationException("WebGPU render pipeline creation failed.")
                    : (nint)pipeline;
            }
        }
        finally
        {
            api.ShaderModuleRelease(shader);
        }
    }

    internal nint GetNativeBindGroupLayout(nint pipeline)
    {
        if (pipeline == 0) { throw new ArgumentException("Pipeline cannot be null.", nameof(pipeline)); }
        BindGroupLayout* layout = api.RenderPipelineGetBindGroupLayout((RenderPipeline*)pipeline, 0);
        return layout is null
            ? throw new InvalidOperationException("WebGPU did not expose bind group layout zero.")
            : (nint)layout;
    }

    internal void ReleaseNativeBindGroupLayout(nint layout)
        => api.BindGroupLayoutRelease((BindGroupLayout*)layout);

    internal void ReleaseNativeRasterPipeline(nint pipeline)
        => api.RenderPipelineRelease((RenderPipeline*)pipeline);

    private ShaderModule* CreateNativeShaderModule(string source)
    {
        byte[] sourceBytes = Encoding.UTF8.GetBytes(source + '\0');
        fixed (byte* code = sourceBytes)
        {
            var wgsl = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct { SType = SType.ShaderModuleWgslDescriptor },
                Code = code,
            };
            var description = new ShaderModuleDescriptor { NextInChain = &wgsl.Chain };
            ShaderModule* shader = api.DeviceCreateShaderModule(device, in description);
            return shader is null
                ? throw new InvalidOperationException("WebGPU WGSL shader module creation failed.")
                : shader;
        }
    }

    private Texture* CreateNativeTexture(uint width, uint height, TextureUsage usage)
    {
        var description = new TextureDescriptor
        {
            Usage = usage,
            Dimension = TextureDimension.Dimension2D,
            Size = new Extent3D(width, height, 1),
            Format = TextureFormat.Rgba8Unorm,
            MipLevelCount = 1,
            SampleCount = 1,
        };
        Texture* texture = api.DeviceCreateTexture(device, in description);
        return texture is null ? throw new InvalidOperationException("WebGPU texture creation failed.") : texture;
    }

    private WgpuBuffer* CreateNativeBuffer(ulong size, BufferUsage usage)
    {
        var description = new BufferDescriptor { Size = size, Usage = usage };
        WgpuBuffer* buffer = api.DeviceCreateBuffer(device, in description);
        return buffer is null ? throw new InvalidOperationException("WebGPU buffer creation failed.") : buffer;
    }

    private static BlendComponent ToWebGpuBlendComponent(GpuBlendDescription? description, bool alpha)
    {
        if (description is null) { return default; }
        return new()
        {
            Operation = ToWebGpuBlendOperation(alpha ? description.AlphaOperation : description.ColorOperation),
            SrcFactor = ToWebGpuBlendFactor(alpha ? description.SourceAlphaFactor : description.SourceColorFactor),
            DstFactor = ToWebGpuBlendFactor(alpha ? description.DestinationAlphaFactor : description.DestinationColorFactor),
        };
    }

    private static BlendOperation ToWebGpuBlendOperation(GpuBlendOperation operation) => operation switch
    {
        GpuBlendOperation.Add => BlendOperation.Add,
        GpuBlendOperation.Subtract => BlendOperation.Subtract,
        GpuBlendOperation.ReverseSubtract => BlendOperation.ReverseSubtract,
        GpuBlendOperation.Minimum => BlendOperation.Min,
        GpuBlendOperation.Maximum => BlendOperation.Max,
        _ => throw new ArgumentOutOfRangeException(nameof(operation)),
    };

    private static BlendFactor ToWebGpuBlendFactor(GpuBlendFactor factor) => factor switch
    {
        GpuBlendFactor.Zero => BlendFactor.Zero,
        GpuBlendFactor.One => BlendFactor.One,
        GpuBlendFactor.SourceColor => BlendFactor.Src,
        GpuBlendFactor.OneMinusSourceColor => BlendFactor.OneMinusSrc,
        GpuBlendFactor.DestinationColor => BlendFactor.Dst,
        GpuBlendFactor.OneMinusDestinationColor => BlendFactor.OneMinusDst,
        GpuBlendFactor.SourceAlpha => BlendFactor.SrcAlpha,
        GpuBlendFactor.OneMinusSourceAlpha => BlendFactor.OneMinusSrcAlpha,
        GpuBlendFactor.DestinationAlpha => BlendFactor.DstAlpha,
        GpuBlendFactor.OneMinusDestinationAlpha => BlendFactor.OneMinusDstAlpha,
        _ => throw new ArgumentOutOfRangeException(nameof(factor)),
    };

    private static ColorWriteMask ToWebGpuColorWriteMask(GpuColorWriteMask mask)
    {
        ColorWriteMask result = 0;
        if ((mask & GpuColorWriteMask.Red) != 0) { result |= ColorWriteMask.Red; }
        if ((mask & GpuColorWriteMask.Green) != 0) { result |= ColorWriteMask.Green; }
        if ((mask & GpuColorWriteMask.Blue) != 0) { result |= ColorWriteMask.Blue; }
        if ((mask & GpuColorWriteMask.Alpha) != 0) { result |= ColorWriteMask.Alpha; }
        return result;
    }
}
