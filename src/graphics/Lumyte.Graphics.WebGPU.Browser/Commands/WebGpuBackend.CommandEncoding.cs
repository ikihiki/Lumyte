using System.Runtime.InteropServices.JavaScript;
using P = Lumyte.Graphics.Portable;

namespace Lumyte.Graphics.WebGPU.Browser;

public sealed partial class WebGpuBackend
{
    private void PreparePipelines(CommandRecording recording, HashSet<Task<IReadOnlyList<P.GpuDiagnostic>>> dependencies)
    {
        PipelineState? selected = null;
        P.GpuRasterPipelineDescription? raster = null;
        int rootLength = 0;
        foreach (RecordedCommand command in recording.Commands)
        {
            switch (command)
            {
                case BeginComputeCommand or BeginRenderingCommand: selected = null; raster = null; rootLength = 0; break;
                case ComputePipelineCommand pipeline: selected = RequireComputePipeline(pipeline.Pipeline).State; raster = null; break;
                case RasterPipelineCommand pipeline:
                    RasterPipelineResource resource = RequireRasterPipeline(pipeline.Pipeline);
                    selected = resource.State; raster = resource.Description; break;
                case ComputeRootCommand root: rootLength = root.Bytes.Length; break;
                case RasterRootCommand root: rootLength = root.Bytes.Length; break;
                case DispatchCommand or DispatchIndirectCommand or DrawCommand or DrawIndexedCommand or DrawIndirectCommand or DrawIndexedIndirectCommand when selected != null:
                    if ((uint)rootLength != selected.ImmediateSize)
                    { throw new InvalidOperationException("Work requires the complete root byte sequence specified by the program's ImmediateSize."); }
                    EnsurePipeline(selected, raster);
                    dependencies.Add(selected.Diagnostics);
                    break;
            }
        }
    }

    private JSObject Encode(CommandRecording recording, HashSet<Task<IReadOnlyList<P.GpuDiagnostic>>> dependencies)
    {
        var references = new List<JSObject>();
        var indices = new Dictionary<JSObject, int>(ReferenceEqualityComparer.Instance);
        object Reference(JSObject handle)
        {
            if (!indices.TryGetValue(handle, out int index)) { index = references.Count; indices.Add(handle, index); references.Add(handle); }
            return Ref(index);
        }
        object Buffer(P.GpuBufferHandle handle)
        {
            BufferResource resource = RequireBuffer(handle); dependencies.Add(resource.Diagnostics); return Reference(resource.Handle);
        }
        object Texture(P.GpuTextureHandle handle)
        {
            TextureResource resource = RequireTexture(handle); dependencies.Add(resource.Diagnostics); return Reference(resource.Handle);
        }
        object Attachment(P.GpuTextureView view)
        {
            TextureViewLease lease = AcquireTextureView(view, 16);
            recording.RetainAttachmentView(lease);
            dependencies.Add(lease.Diagnostics);
            return Reference(lease.Handle);
        }
        var commands = new List<object>();
        PipelineState? selected = null;
        bool pipelineChanged = false;
        foreach (RecordedCommand command in recording.Commands)
        {
            if (command is DrawCommand or DrawIndexedCommand or DrawIndirectCommand or DrawIndexedIndirectCommand or DispatchCommand or DispatchIndirectCommand)
            {
                if (pipelineChanged && selected != null)
                { commands.Add(new { op = "setPipeline", pipeline = Reference(selected.Handle!) }); pipelineChanged = false; }
            }
            switch (command)
            {
                case BeginComputeCommand:
                    selected = null; pipelineChanged = false; commands.Add(new { op = "beginCompute" }); break;
                case EndComputeCommand: commands.Add(new { op = "endCompute" }); break;
                case BeginRenderingCommand begin:
                    selected = null; pipelineChanged = false;
                    commands.Add(new
                    {
                        op = "beginRendering", descriptor = new
                        {
                            colorAttachments = begin.Colors.Select(color => new
                            {
                                view = Attachment(color.View), loadOp = MapLoad(color.LoadOperation), storeOp = MapStore(color.StoreOperation),
                                clearValue = MapClearColor(color.ClearColor), depthSlice = color.DepthSlice,
                                resolveTarget = color.ResolveTarget is { } resolve ? Attachment(resolve) : null,
                            }).ToArray(),
                            depthStencilAttachment = begin.DepthStencil is { } depth ? new
                            {
                                view = Attachment(depth.View), depthReadOnly = depth.DepthReadOnly, stencilReadOnly = depth.StencilReadOnly,
                                depthLoadOp = depth.DepthLoadOperation is { } dl ? MapLoad(dl) : null,
                                depthStoreOp = depth.DepthStoreOperation is { } ds ? MapStore(ds) : null,
                                stencilLoadOp = depth.StencilLoadOperation is { } sl ? MapLoad(sl) : null,
                                stencilStoreOp = depth.StencilStoreOperation is { } ss ? MapStore(ss) : null,
                                depthClearValue = depth.ClearValue.Depth, stencilClearValue = depth.ClearValue.Stencil,
                            } : null,
                        },
                    });
                    break;
                case EndRenderingCommand: commands.Add(new { op = "endRendering" }); break;
                case ComputePipelineCommand pipeline: selected = RequireComputePipeline(pipeline.Pipeline).State; pipelineChanged = true; break;
                case RasterPipelineCommand pipeline: selected = RequireRasterPipeline(pipeline.Pipeline).State; pipelineChanged = true; break;
                case ComputeRootCommand root: commands.Add(new { op = "setRoot", data = Convert.ToBase64String(root.Bytes) }); break;
                case RasterRootCommand root: commands.Add(new { op = "setRoot", data = Convert.ToBase64String(root.Bytes) }); break;
                case ComputeBindingsCommand binding: AddBindings(binding.Group, binding.Bindings, binding.DynamicOffsets); break;
                case RasterBindingsCommand binding: AddBindings(binding.Group, binding.Bindings, binding.DynamicOffsets); break;
                case ViewportScissorCommand region:
                    P.GpuViewport v = region.Viewport; P.GpuScissorRect s = region.Scissor;
                    commands.Add(new { op = "viewport", x = v.X, y = v.Y, width = v.Width, height = v.Height, minDepth = v.MinDepth, maxDepth = v.MaxDepth });
                    commands.Add(new { op = "scissor", x = s.X, y = s.Y, width = s.Width, height = s.Height }); break;
                case StencilReferenceCommand stencil: commands.Add(new { op = "stencil", value = stencil.Reference }); break;
                case BlendConstantCommand blend: commands.Add(new { op = "blend", color = MapClearColor(blend.Color) }); break;
                case DrawCommand draw:
                    commands.Add(new { op = "draw", vertexCount = draw.VertexCount, instanceCount = draw.InstanceCount, firstVertex = draw.FirstVertex, firstInstance = draw.FirstInstance }); break;
                case DrawIndexedCommand draw:
                    commands.Add(new
                    {
                        op = "drawIndexed", indices = Buffer(draw.Indices.Buffer), format = MapIndexFormat(draw.Format),
                        offset = Exact(draw.Indices.Offset, "indices"), size = Exact(draw.Indices.Length!.Value, "indices"),
                        indexCount = draw.IndexCount, instanceCount = draw.InstanceCount, firstIndex = draw.FirstIndex, baseVertex = draw.BaseVertex, firstInstance = draw.FirstInstance,
                    }); break;
                case DrawIndirectCommand draw:
                    commands.Add(new { op = "drawIndirect", arguments = Buffer(draw.Arguments.Buffer), offset = Exact(draw.Arguments.Offset, "arguments") }); break;
                case DrawIndexedIndirectCommand draw:
                    commands.Add(new
                    {
                        op = "drawIndexedIndirect", indices = Buffer(draw.Indices.Buffer), format = MapIndexFormat(draw.Format),
                        offset = Exact(draw.Indices.Offset, "indices"), size = Exact(draw.Indices.Length!.Value, "indices"),
                        arguments = Buffer(draw.Arguments.Buffer), argumentsOffset = Exact(draw.Arguments.Offset, "arguments"),
                    }); break;
                case DispatchCommand dispatch: commands.Add(new { op = "dispatch", x = dispatch.X, y = dispatch.Y, z = dispatch.Z }); break;
                case DispatchIndirectCommand dispatch:
                    commands.Add(new { op = "dispatchIndirect", arguments = Buffer(dispatch.Arguments.Buffer), offset = Exact(dispatch.Arguments.Offset, "arguments") }); break;
                case CopyBufferCommand copy:
                    commands.Add(new
                    {
                        op = "copyBuffer", source = Buffer(copy.Source.Buffer), sourceOffset = Exact(copy.Source.Offset, "source"),
                        destination = Buffer(copy.Destination.Buffer), destinationOffset = Exact(copy.Destination.Offset, "destination"), size = Exact(copy.Source.Length!.Value, "source"),
                    }); break;
                case CopyBufferToTextureCommand copy:
                    commands.Add(new { op = "copyBufferToTexture", source = BufferCopyInfo(Buffer(copy.Source.Buffer), copy.Source.Offset, copy.Footprint),
                        destination = TextureCopyInfo(Texture(copy.Texture), copy.Footprint), extent = CopyExtent(copy.Footprint) }); break;
                case CopyTextureToBufferCommand copy:
                    commands.Add(new { op = "copyTextureToBuffer", source = TextureCopyInfo(Texture(copy.Texture), copy.Footprint),
                        destination = BufferCopyInfo(Buffer(copy.Destination.Buffer), copy.Destination.Offset, copy.Footprint), extent = CopyExtent(copy.Footprint) }); break;
                case CopyTextureCommand copy:
                    commands.Add(new { op = "copyTexture", source = TextureCopyInfo(Texture(copy.Source), copy.SourceFootprint),
                        destination = TextureCopyInfo(Texture(copy.Destination), copy.DestinationFootprint), extent = CopyExtent(copy.SourceFootprint) }); break;
                default: throw new InvalidOperationException("The recording contains an unknown command.");
            }
        }
        string json = Json(commands);
        JSObject[] objects = references.ToArray();
        BrowserInterop.PushErrorScopes(device);
        JSObject? result = null;
        try
        {
            try { result = BrowserInterop.Encode(device, json, objects); }
            finally { dependencies.Add(ReadDiagnosticsAsync(BrowserInterop.PopErrorScopesAsync(device))); }
            return result;
        }
        catch (JSException error)
        {
            result?.Dispose();
            throw new P.GpuOperationException("Encode", [new(P.GpuDiagnosticKind.Runtime, error.Message)]);
        }
        catch { result?.Dispose(); throw; }

        void AddBindings(uint group, P.GpuBindingsHandle handle, uint[] offsets)
        {
            BindingsResource binding = RequireBindings(handle); dependencies.Add(binding.Diagnostics);
            commands.Add(new { op = "setBindings", group, bindings = Reference(binding.Handle), offsets });
        }
    }

    private static object MapClearColor(P.GpuClearColor color) => new { r = color.Red, g = color.Green, b = color.Blue, a = color.Alpha };
    private static string MapLoad(P.GpuAttachmentLoadOperation op) => op switch
    { P.GpuAttachmentLoadOperation.Load => "load", P.GpuAttachmentLoadOperation.Clear => "clear", _ => throw new ArgumentOutOfRangeException(nameof(op)) };
    private static string MapStore(P.GpuAttachmentStoreOperation op) => op switch
    { P.GpuAttachmentStoreOperation.Store => "store", P.GpuAttachmentStoreOperation.Discard => "discard", _ => throw new ArgumentOutOfRangeException(nameof(op)) };
    private static object TextureCopyInfo(object texture, P.GpuTextureCopyFootprint footprint) => new
    { texture, mipLevel = footprint.Mip, aspect = MapTextureAspect(footprint.Aspect), origin = new { x = footprint.Origin.X, y = footprint.Origin.Y, z = footprint.Origin.Z } };
    private static object CopyExtent(P.GpuTextureCopyFootprint footprint) => new
    { width = footprint.Extent.Width, height = footprint.Extent.Height, depthOrArrayLayers = footprint.Extent.Depth };
    private static (uint? BytesPerRow, uint? RowsPerImage) MapTextureCopyLayout(ulong offset, P.GpuTextureCopyFootprint footprint)
    {
        Exact(offset, nameof(offset));
        uint? rowPitch = footprint.RowPitch == 0 ? null : checked((uint)footprint.RowPitch);
        uint? rows = null;
        if (footprint.ImagePitch != 0)
        {
            if (footprint.RowPitch == 0 || footprint.ImagePitch % footprint.RowPitch != 0)
            { throw new ArgumentException("ImagePitch must be a whole multiple of an explicit RowPitch to map without loss.", nameof(footprint)); }
            rows = checked((uint)(footprint.ImagePitch / footprint.RowPitch));
        }
        return (rowPitch, rows);
    }
    private static object BufferCopyInfo(object buffer, ulong offset, P.GpuTextureCopyFootprint footprint)
    {
        var layout = MapTextureCopyLayout(offset, footprint);
        return new { buffer, offset = Exact(offset, nameof(offset)), bytesPerRow = layout.BytesPerRow, rowsPerImage = layout.RowsPerImage };
    }
}
