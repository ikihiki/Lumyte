using System.Text;
using System.Text.RegularExpressions;

using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.WGPU;

namespace Lumyte.Graphics.WebGPU;

public sealed unsafe partial class WebGpuDevice
{
    public GpuRasterPipelineHandle CreateRasterPipeline(
        GpuRasterPipelineDescription description,
        GpuShaderPackage package,
        string vertexEntryPoint,
        string pixelEntryPoint,
        ReadOnlyMemory<byte> expectedAbiHash)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        description.Validate();
        ArgumentNullException.ThrowIfNull(package);
        if (description.ColorTargets.Count != 1 || description.SampleCount != 1)
        {
            throw new NotSupportedException("The current WebGPU raster slice supports one single-sampled color target.");
        }
        if (description.AlphaToCoverage || description.SupportsDualSourceBlending)
        {
            throw new NotSupportedException("The current WebGPU raster slice does not support alpha-to-coverage or dual-source blending.");
        }
        if (description.DepthFormat is { } depth && description.StencilFormat is { } stencil && depth != stencil)
        {
            throw new NotSupportedException("WebGPU requires depth and stencil aspects to use one attachment format.");
        }

        GpuShaderArtifact vertex = package.Select(
            GpuShaderCodeFormat.Wgsl, GpuShaderStage.Vertex, vertexEntryPoint, expectedAbiHash.Span);
        GpuShaderArtifact pixel = package.Select(
            GpuShaderCodeFormat.Wgsl, GpuShaderStage.Pixel, pixelEntryPoint, expectedAbiHash.Span);
        if (!vertex.Payload.Span.SequenceEqual(pixel.Payload.Span))
        {
            throw new InvalidOperationException("WebGPU vertex and pixel artifacts must contain the same WGSL module.");
        }

        string source = TranslateLogicalBindings(Encoding.UTF8.GetString(vertex.Payload.Span));
        nint native = CreateNativeRasterPipeline(
            source,
            vertexEntryPoint,
            pixelEntryPoint,
            description);
        var handle = new GpuRasterPipelineHandle(nextPipelineId++);
        rasterPipelines.Add(handle.Value, new(native));
        return handle;
    }

    public GpuComputePipelineHandle CreateComputePipeline(
        GpuShaderPackage package,
        string entryPoint,
        ReadOnlyMemory<byte> expectedAbiHash)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(package);
        GpuShaderArtifact artifact = package.Select(
            GpuShaderCodeFormat.Wgsl, GpuShaderStage.Compute, entryPoint, expectedAbiHash.Span);
        string source = TranslateLogicalBindings(Encoding.UTF8.GetString(artifact.Payload.Span));
        ShaderModule* shader = CreateNativeShaderModule(source);
        try
        {
            byte[] entryBytes = Encoding.UTF8.GetBytes(entryPoint + '\0');
            fixed (byte* nativeEntry = entryBytes)
            {
                var description = new ComputePipelineDescriptor
                {
                    Compute = new ProgrammableStageDescriptor
                    {
                        Module = shader,
                        EntryPoint = nativeEntry,
                    },
                };
                ComputePipeline* native = api.DeviceCreateComputePipeline(device, in description);
                if (native is null) { throw new InvalidOperationException("WebGPU compute pipeline creation failed."); }
                var handle = new GpuComputePipelineHandle(nextPipelineId++);
                computePipelines.Add(handle.Value, new((nint)native));
                return handle;
            }
        }
        finally
        {
            api.ShaderModuleRelease(shader);
        }
    }

    public void DestroyComputePipeline(GpuComputePipelineHandle pipeline)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!computePipelines.Remove(pipeline.Value, out ComputePipelineRecord? record))
        {
            throw new ArgumentException("Pipeline does not belong to this WebGPU device.", nameof(pipeline));
        }
        InvalidateBindGroups();
        record.Dispose(api);
    }

    private static string TranslateLogicalBindings(string source)
    {
        source = LogicalBindingFirstPattern().Replace(source, static match => TranslateBinding(match));
        return LogicalGroupFirstPattern().Replace(source, static match => TranslateBinding(match));
    }

    private static string TranslateBinding(Match match)
    {
        int binding = int.Parse(match.Groups["binding"].Value, System.Globalization.CultureInfo.InvariantCulture);
        int group = int.Parse(match.Groups["group"].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (group == GpuShaderBindingConvention.TextureTable && binding >= MaximumShaderDescriptors)
        {
            return match.Value;
        }
        if (binding >= MaximumShaderDescriptors)
        {
            throw new NotSupportedException(
                $"WebGPU logical shader group {group} cannot address binding {binding}.");
        }
        int nativeBinding = group switch
        {
            GpuShaderBindingConvention.TextureTable => binding,
            GpuShaderBindingConvention.SamplerTable => checked(NativeSamplerBindingOffset + binding),
            GpuShaderBindingConvention.BufferTable => checked(NativeBufferBindingOffset + binding),
            GpuShaderBindingConvention.StorageTextureTable => checked(NativeStorageTextureBindingOffset + binding),
            GpuShaderBindingConvention.WritableBufferTable => checked(NativeWritableBufferBindingOffset + binding),
            _ => throw new NotSupportedException($"WebGPU cannot translate logical shader group {group}."),
        };
        return $"@binding({nativeBinding}) @group(0)";
    }

    [GeneratedRegex(@"@binding\((?<binding>\d+)\)\s+@group\((?<group>\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalBindingFirstPattern();

    [GeneratedRegex(@"@group\((?<group>\d+)\)\s+@binding\((?<binding>\d+)\)", RegexOptions.CultureInvariant)]
    private static partial Regex LogicalGroupFirstPattern();

    public void DestroyRasterPipeline(GpuRasterPipelineHandle pipeline)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!rasterPipelines.Remove(pipeline.Value, out RasterPipelineRecord? record))
        {
            throw new ArgumentException("Pipeline does not belong to this WebGPU device.", nameof(pipeline));
        }
        InvalidateBindGroups();
        record.Dispose(api);
    }

    private WebGpuCommandRecorder BeginCommands()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var description = new CommandEncoderDescriptor();
        CommandEncoder* encoder = api.DeviceCreateCommandEncoder(device, in description);
        return encoder is null
            ? throw new InvalidOperationException("WebGPU command encoder creation failed.")
            : new(this, encoder);
    }

    private sealed class WebGpuQueue(WebGpuDevice owner) : IGpuQueue
    {
        public GpuCommandBuffer StartCommandRecording() => GpuBackendCommands.CreateCommandBuffer(owner.BeginCommands());

        public GpuSemaphore CreateSemaphore(ulong initialValue = 0) => new WebGpuSemaphore(owner, initialValue);

        public void Submit(ReadOnlySpan<GpuCommandBuffer> commandBuffers, GpuSemaphore signalSemaphore, ulong signalValue)
        {
            if (commandBuffers.IsEmpty) { throw new ArgumentException("At least one command buffer is required.", nameof(commandBuffers)); }
            if (signalSemaphore is not WebGpuSemaphore semaphore || semaphore.Owner != owner)
            {
                throw new ArgumentException("Semaphore belongs to another backend.", nameof(signalSemaphore));
            }
            semaphore.ValidateSignal(signalValue);
            CommandBuffer** native = stackalloc CommandBuffer*[commandBuffers.Length];
            var records = new nint[commandBuffers.Length];
            for (int index = 0; index < commandBuffers.Length; index++)
            {
                if (GpuBackendCommands.Finish(commandBuffers[index]) is not WebGpuCommandRecorder recorder || recorder.Owner != owner)
                {
                    throw new ArgumentException("Command buffer belongs to another backend.", nameof(commandBuffers));
                }
                native[index] = recorder.Commands;
                records[index] = (nint)recorder.Commands;
            }
            owner.api.QueueSubmit(owner.queue, checked((nuint)commandBuffers.Length), native);
            semaphore.Track(signalValue, records);
        }

        public void Wait(GpuSemaphore signalSemaphore, ulong value)
        {
            if (signalSemaphore is not WebGpuSemaphore semaphore || semaphore.Owner != owner)
            {
                throw new ArgumentException("Semaphore belongs to another backend.", nameof(signalSemaphore));
            }
            semaphore.Wait(value);
        }

        public bool IsComplete(GpuSemaphore signalSemaphore, ulong value)
        {
            if (signalSemaphore is not WebGpuSemaphore semaphore || semaphore.Owner != owner)
            {
                throw new ArgumentException("Semaphore belongs to another backend.", nameof(signalSemaphore));
            }
            return semaphore.IsComplete(value);
        }
    }

    private sealed class WebGpuCommandRecorder : IGpuCommandRecorder
    {
        private CommandEncoder* encoder;
        private RenderPassEncoder* pass;
        private ComputePassEncoder* computePass;
        private RasterPipelineRecord? pipeline;
        private ComputePipelineRecord? computePipeline;

        public WebGpuCommandRecorder(WebGpuDevice owner, CommandEncoder* encoder)
        {
            Owner = owner;
            this.encoder = encoder;
        }

        public WebGpuDevice Owner { get; }
        public CommandBuffer* Commands { get; private set; }

        public void Barrier(GpuStage before, GpuStage after, GpuBarrierHazards hazards)
            => EndComputePass();

        public void BeginRendering(IReadOnlyList<GpuColorAttachment> colors, GpuDepthStencilAttachment? depth)
        {
            EndComputePass();
            if (colors.Count != 1)
            {
                throw new NotSupportedException("The current WebGPU raster slice supports one color attachment.");
            }
            GpuColorAttachment attachment = colors[0];
            if (!Owner.textureViews.TryGetValue(attachment.View.Id.Value, out TextureViewRecord? view)
                || view.View != attachment.View)
            {
                throw new ArgumentException("Attachment view does not belong to this WebGPU device.", nameof(colors));
            }
            var nativeAttachment = new RenderPassColorAttachment
            {
                View = (TextureView*)view.Handle,
                DepthSlice = uint.MaxValue,
                LoadOp = attachment.LoadOperation switch
                {
                    GpuAttachmentLoadOperation.Load => LoadOp.Load,
                    GpuAttachmentLoadOperation.Clear => LoadOp.Clear,
                    GpuAttachmentLoadOperation.Discard => LoadOp.Clear,
                    _ => throw new ArgumentOutOfRangeException(nameof(colors)),
                },
                StoreOp = attachment.StoreOperation == GpuAttachmentStoreOperation.Store ? StoreOp.Store : StoreOp.Discard,
                ClearValue = new Color(
                    attachment.ClearColor.Red,
                    attachment.ClearColor.Green,
                    attachment.ClearColor.Blue,
                    attachment.ClearColor.Alpha),
            };
            var description = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &nativeAttachment,
            };
            RenderPassDepthStencilAttachment nativeDepth = default;
            if (depth is { } depthAttachment)
            {
                if (!Owner.textureViews.TryGetValue(depthAttachment.View.Id.Value, out TextureViewRecord? depthView)
                    || depthView.View != depthAttachment.View)
                {
                    throw new ArgumentException("Depth-stencil view does not belong to this WebGPU device.", nameof(depth));
                }
                nativeDepth = new()
                {
                    View = (TextureView*)depthView.Handle,
                    DepthLoadOp = GpuBackendCommands.HasDepth(depthAttachment.View.Description.Format)
                        ? ToLoadOp(depthAttachment.LoadOperation)
                        : LoadOp.Undefined,
                    DepthStoreOp = GpuBackendCommands.HasDepth(depthAttachment.View.Description.Format)
                        ? ToStoreOp(depthAttachment.StoreOperation)
                        : StoreOp.Undefined,
                    DepthClearValue = depthAttachment.ClearValue.Depth,
                    DepthReadOnly = !GpuBackendCommands.HasDepth(depthAttachment.View.Description.Format),
                    StencilLoadOp = GpuBackendCommands.HasStencil(depthAttachment.View.Description.Format)
                        ? ToLoadOp(depthAttachment.LoadOperation)
                        : LoadOp.Undefined,
                    StencilStoreOp = GpuBackendCommands.HasStencil(depthAttachment.View.Description.Format)
                        ? ToStoreOp(depthAttachment.StoreOperation)
                        : StoreOp.Undefined,
                    StencilClearValue = depthAttachment.ClearValue.Stencil,
                    StencilReadOnly = !GpuBackendCommands.HasStencil(depthAttachment.View.Description.Format),
                };
                description.DepthStencilAttachment = &nativeDepth;
            }
            pass = Owner.api.CommandEncoderBeginRenderPass(encoder, in description);
            if (pass is null) { throw new InvalidOperationException("WebGPU render pass creation failed."); }
        }

        public void EndRendering()
        {
            Owner.api.RenderPassEncoderEnd(pass);
            Owner.api.RenderPassEncoderRelease(pass);
            pass = null;
        }

        public void SetPipeline(GpuRasterPipelineHandle handle)
        {
            if (!Owner.rasterPipelines.TryGetValue(handle.Value, out RasterPipelineRecord? record))
            {
                throw new ArgumentException("Pipeline does not belong to this WebGPU device.", nameof(handle));
            }
            pipeline = record;
            Owner.api.RenderPassEncoderSetPipeline(pass, (RenderPipeline*)record.Handle);
        }

        public void SetViewportAndScissor(GpuViewport viewport, GpuScissorRect scissor)
        {
            Owner.api.RenderPassEncoderSetViewport(pass, viewport.X, viewport.Y, viewport.Width, viewport.Height, viewport.MinDepth, viewport.MaxDepth);
            Owner.api.RenderPassEncoderSetScissorRect(pass, scissor.X, scissor.Y, scissor.Width, scissor.Height);
        }

        public void Draw(uint vertexCount, uint instanceCount)
            => Owner.api.RenderPassEncoderDraw(pass, vertexCount, instanceCount, 0, 0);

        public void SetResourceTable(GpuResourceTable table)
        {
            if (pipeline is null) { throw new InvalidOperationException("A raster pipeline must be bound before a resource table."); }
            pipeline.Layout = pipeline.Layout == 0
                ? Owner.GetNativeBindGroupLayout(pipeline.Handle)
                : pipeline.Layout;
            nint bindGroup = Owner.GetOrCreateBindGroup(table, pipeline.Layout);
            Owner.api.RenderPassEncoderSetBindGroup(pass, 0, (BindGroup*)bindGroup, 0, null);
        }

        public void CopyMemoryToTexture(GpuMemoryAddress source, GpuTextureHandle destination, GpuTextureCopyFootprint footprint)
            => throw new NotSupportedException("WebGPU uses device-owned texture writes.");

        public void CopyTextureToMemory(GpuTextureHandle source, GpuMemoryAddress destination, GpuTextureCopyFootprint footprint)
            => throw new NotSupportedException("WebGPU uses device-owned texture reads.");

        public void SetRootData(ReadOnlySpan<byte> data)
            => throw new NotSupportedException("WebGPU root data mapping is not implemented.");

        public void SetComputePipeline(GpuComputePipelineHandle handle)
        {
            if (!Owner.computePipelines.TryGetValue(handle.Value, out ComputePipelineRecord? record))
            {
                throw new ArgumentException("Pipeline does not belong to this WebGPU device.", nameof(handle));
            }
            if (computePass is null)
            {
                var description = new ComputePassDescriptor();
                computePass = Owner.api.CommandEncoderBeginComputePass(encoder, in description);
                if (computePass is null) { throw new InvalidOperationException("WebGPU compute pass creation failed."); }
            }
            computePipeline = record;
            Owner.api.ComputePassEncoderSetPipeline(computePass, (ComputePipeline*)record.Handle);
        }

        public void SetComputeResourceTable(GpuResourceTable table)
        {
            if (computePipeline is null || computePass is null)
            {
                throw new InvalidOperationException("A compute pipeline must be bound before a compute resource table.");
            }
            computePipeline.Layout = computePipeline.Layout == 0
                ? (nint)Owner.api.ComputePipelineGetBindGroupLayout((ComputePipeline*)computePipeline.Handle, 0)
                : computePipeline.Layout;
            if (computePipeline.Layout == 0)
            {
                throw new InvalidOperationException("WebGPU did not expose compute bind group layout zero.");
            }
            nint bindGroup = Owner.GetOrCreateBindGroup(table, computePipeline.Layout);
            Owner.api.ComputePassEncoderSetBindGroup(computePass, 0, (BindGroup*)bindGroup, 0, null);
        }

        public void SetComputeRootData(ReadOnlySpan<byte> data)
            => throw new NotSupportedException(
                "WebGPU has no push constants; compute parameters must use the read-only buffer table.");

        public void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
        {
            if (computePipeline is null || computePass is null)
            {
                throw new InvalidOperationException("A compute pipeline must be bound before dispatch.");
            }
            Owner.api.ComputePassEncoderDispatchWorkgroups(computePass, groupCountX, groupCountY, groupCountZ);
        }

        public void End()
        {
            EndComputePass();
            var description = new CommandBufferDescriptor();
            Commands = Owner.api.CommandEncoderFinish(encoder, in description);
            Owner.api.CommandEncoderRelease(encoder);
            encoder = null;
            if (Commands is null) { throw new InvalidOperationException("WebGPU command buffer creation failed."); }
        }

        private void EndComputePass()
        {
            if (computePass is null) { return; }
            Owner.api.ComputePassEncoderEnd(computePass);
            Owner.api.ComputePassEncoderRelease(computePass);
            computePass = null;
            computePipeline = null;
        }

        private static LoadOp ToLoadOp(GpuAttachmentLoadOperation operation) => operation switch
        {
            GpuAttachmentLoadOperation.Load => LoadOp.Load,
            GpuAttachmentLoadOperation.Clear => LoadOp.Clear,
            GpuAttachmentLoadOperation.Discard => LoadOp.Clear,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        private static StoreOp ToStoreOp(GpuAttachmentStoreOperation operation) => operation switch
        {
            GpuAttachmentStoreOperation.Store => StoreOp.Store,
            GpuAttachmentStoreOperation.Discard => StoreOp.Discard,
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
    }

    private sealed class WebGpuSemaphore(WebGpuDevice owner, ulong initialValue) : GpuSemaphore
    {
        private readonly SortedDictionary<ulong, List<nint>> pending = [];
        private ulong completedValue = initialValue;
        private ulong lastSignalValue = initialValue;
        private bool disposed;

        public WebGpuDevice Owner { get; } = owner;

        public void ValidateSignal(ulong value)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (value <= lastSignalValue) { throw new ArgumentOutOfRangeException(nameof(value)); }
        }

        public void Track(ulong value, nint[] commands)
        {
            lastSignalValue = value;
            pending.Add(value, [.. commands]);
        }

        public void Wait(ulong value)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (value > lastSignalValue) { throw new ArgumentOutOfRangeException(nameof(value)); }
            if (value > completedValue)
            {
                var extensions = new Wgpu(Owner.api.Context);
                extensions.DevicePoll(Owner.device, true, null);
                completedValue = lastSignalValue;
            }
            ReleaseCompleted();
        }

        public bool IsComplete(ulong value)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (value > lastSignalValue) { throw new ArgumentOutOfRangeException(nameof(value)); }
            if (value > completedValue)
            {
                var extensions = new Wgpu(Owner.api.Context);
                if (extensions.DevicePoll(Owner.device, false, null))
                {
                    completedValue = lastSignalValue;
                }
            }
            ReleaseCompleted();
            return value <= completedValue;
        }

        private void ReleaseCompleted()
        {
            foreach (ulong key in pending.Keys.TakeWhile(key => key <= completedValue).ToArray())
            {
                foreach (nint command in pending[key]) { Owner.api.CommandBufferRelease((CommandBuffer*)command); }
                pending.Remove(key);
            }
        }

        public override void Dispose()
        {
            if (pending.Count != 0) { throw new InvalidOperationException("Semaphore still owns in-flight command buffers."); }
            disposed = true;
        }
    }

    private sealed class RasterPipelineRecord(nint handle)
    {
        public nint Handle { get; } = handle;
        public nint Layout { get; set; }

        public void Dispose(Silk.NET.WebGPU.WebGPU api)
        {
            if (Layout != 0) { api.BindGroupLayoutRelease((BindGroupLayout*)Layout); }
            api.RenderPipelineRelease((RenderPipeline*)Handle);
        }
    }

    private sealed class ComputePipelineRecord(nint handle)
    {
        public nint Handle { get; } = handle;
        public nint Layout { get; set; }

        public void Dispose(Silk.NET.WebGPU.WebGPU api)
        {
            if (Layout != 0) { api.BindGroupLayoutRelease((BindGroupLayout*)Layout); }
            api.ComputePipelineRelease((ComputePipeline*)Handle);
        }
    }
}
