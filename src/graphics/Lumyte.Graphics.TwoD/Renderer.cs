using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.TwoD;

/// <summary>Owns 2D pipelines and prepares immutable display lists for one GPU backend.</summary>
public sealed class Renderer : IDisposable
{
    private const int ShaderBufferOffsetAlignment = 256;
    private const int LayerParameterSlots = 6;

    private readonly IGpuBackend backend;
    private readonly Dictionary<ImageId, RegisteredImage> images = [];
    private readonly Dictionary<DistanceFieldAtlas, ImageId> distanceFieldAtlases = [];
    private readonly Dictionary<(GpuFormat Format, PreparedBatchKind Kind), GpuRasterPipelineHandle> pipelines = [];
    private readonly Dictionary<(GpuFormat Format, BlendMode Mode), GpuRasterPipelineHandle> layerPipelines = [];
    private readonly Dictionary<GpuFormat, GpuRasterPipelineHandle> blurPipelines = [];
    private readonly Dictionary<GpuFormat, GpuRasterPipelineHandle> maskPipelines = [];
    private GpuComputePipelineHandle pathPreparationPipeline;
    private SamplerId layerSampler;
    private GpuShaderPackage? phaseOneShaders;
    private GpuShaderPackage? phaseThreeShaders;
    private GpuShaderPackage? phaseFourShaders;
    private ulong nextImageId = 1;
    private bool disposed;

    internal IGpuBackend Backend => backend;

    public Renderer(IGpuBackend backend)
    {
        this.backend = backend ?? throw new ArgumentNullException(nameof(backend));
        if ((backend.Capabilities & GpuBackendCapabilities.RasterPipeline) == 0)
        {
            throw new ArgumentException("The backend does not support raster pipelines.", nameof(backend));
        }
    }

    public CommandEncoder CreateCommandEncoder()
    {
        VerifyAlive();
        return new(this);
    }

    public ImageId RegisterImage(
        GpuTextureHandle texture,
        GpuTextureDescription description,
        SamplerId sampler)
    {
        VerifyAlive();
        description.Validate();
        if (texture.IsNull) { throw new ArgumentException("Image texture cannot be null.", nameof(texture)); }
        if (sampler.IsNull) { throw new ArgumentException("Image sampler cannot be null.", nameof(sampler)); }
        if ((description.Usage & GpuTextureUsage.Sampled) == 0)
        {
            throw new ArgumentException("Image texture requires sampled usage.", nameof(description));
        }

        var id = new ImageId(nextImageId++);
        images.Add(id, new(texture, description, sampler));
        return id;
    }

    public void UnregisterImage(ImageId image)
    {
        VerifyAlive();
        if (image.IsNull || !images.Remove(image))
        {
            throw new ArgumentException("Image is not registered with this renderer.", nameof(image));
        }
    }

    public PreparedDisplayList Prepare(
        DisplayList displayList,
        GpuTextureDescription targetDescription)
    {
        VerifyAlive();
        ArgumentNullException.ThrowIfNull(displayList);
        if (!ReferenceEquals(displayList.Owner, this))
        {
            throw new ArgumentException("Display list belongs to another renderer.", nameof(displayList));
        }
        targetDescription.Validate();
        if ((targetDescription.Usage & GpuTextureUsage.ColorAttachment) == 0)
        {
            throw new ArgumentException("The target requires color-attachment usage.", nameof(targetDescription));
        }

        var gpuCommands = new List<GpuDrawCommand>(displayList.Count);
        var polygonBytes = new ArrayBufferWriter<byte>();
        var pathInputBytes = new ArrayBufferWriter<byte>();
        var pathJobs = new List<PathComputeJob>();
        ulong pathOutputLength = 0;
        var batches = new List<PreparedBatch>();
        var preparedImages = new List<PreparedImage>();
        var imageIndices = new Dictionary<ImageId, int>();
        var targetBounds = new Rect(0, 0, targetDescription.Width, targetDescription.Height);
        int acceptedCommands = 0;

        ReadOnlySpan<RecordedCommand> recordedCommands = displayList.Commands;
        for (int sequence = 0; sequence < recordedCommands.Length; sequence++)
        {
            RecordedCommand command = recordedCommands[sequence];
            Rect? clip = command.Clip is { } requestedClip
                ? Rect.Intersect(targetBounds, requestedClip)
                : targetBounds;
            if (clip is null
                || Rect.Intersect(clip.Value, command.Bounds.TransformBounds(command.Transform)) is null)
            {
                continue;
            }

            if (command.Kind == DrawCommandKind.Polygon)
            {
                AddPolygon(command, clip.Value, targetDescription, polygonBytes, batches, sequence);
            }
            else if (command.Kind == DrawCommandKind.Path)
            {
                AddPath(
                    command,
                    clip.Value,
                    targetDescription,
                    pathInputBytes,
                    batches,
                    pathJobs,
                    ref pathOutputLength,
                    sequence);
            }
            else
            {
                int imageIndex = command.Kind is DrawCommandKind.Image or DrawCommandKind.DistanceField
                    ? GetImageIndex(command.Image, preparedImages, imageIndices)
                    : -1;
                PreparedBatchKind kind = command.Kind switch
                {
                    DrawCommandKind.Image => PreparedBatchKind.Image,
                    DrawCommandKind.DistanceField => PreparedBatchKind.DistanceField,
                    _ => PreparedBatchKind.Primitive,
                };
                ulong commandOffset = checked((ulong)gpuCommands.Count * GpuDrawCommand.Size);
                if (!CanAppendBatch(
                    batches,
                    kind,
                    commandOffset,
                    clip.Value,
                    imageIndex,
                    command.LayerId))
                {
                    while (commandOffset % ShaderBufferOffsetAlignment != 0)
                    {
                        gpuCommands.Add(default);
                        commandOffset = checked((ulong)gpuCommands.Count * GpuDrawCommand.Size);
                    }
                }
                int commandIndex = gpuCommands.Count;
                gpuCommands.Add(CreateGpuCommand(command, targetDescription));
                AddBatch(
                    batches,
                    kind,
                    checked((ulong)commandIndex * GpuDrawCommand.Size),
                    GpuDrawCommand.Size,
                    clip.Value,
                    imageIndex,
                    command.LayerId,
                    sequence);
            }
            acceptedCommands++;
        }

        OwnedBuffer? primitiveBuffer = null;
        OwnedBuffer? polygonBuffer = null;
        OwnedBuffer? pathBuffer = null;
        OwnedBuffer? layerBuffer = null;
        try
        {
            if (gpuCommands.Count != 0)
            {
                ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(gpuCommands));
                primitiveBuffer = OwnedBuffer.Create(backend, bytes);
            }
            if (polygonBytes.WrittenCount != 0)
            {
                polygonBuffer = OwnedBuffer.Create(backend, polygonBytes.WrittenSpan);
            }
            if (pathInputBytes.WrittenCount != 0)
            {
                using OwnedBuffer input = OwnedBuffer.Create(backend, pathInputBytes.WrittenSpan);
                pathBuffer = OwnedBuffer.CreateStorage(backend, pathOutputLength);
                PreparePaths(input, pathBuffer, pathJobs);
            }
            PreparedLayer[] preparedLayers = PrepareLayers(
                displayList.Layers,
                targetDescription,
                preparedImages,
                imageIndices,
                out byte[] layerBytes);
            if (layerBytes.Length != 0)
            {
                layerBuffer = OwnedBuffer.Create(backend, layerBytes);
            }
            return new(
                this,
                targetDescription,
                acceptedCommands,
                batches.ToArray(),
                preparedImages.ToArray(),
                preparedLayers,
                primitiveBuffer,
                polygonBuffer,
                pathBuffer,
                layerBuffer);
        }
        catch
        {
            layerBuffer?.Dispose();
            pathBuffer?.Dispose();
            polygonBuffer?.Dispose();
            primitiveBuffer?.Dispose();
            throw;
        }
    }

    public SceneSnapshot Prepare(Scene scene, GpuTextureDescription targetDescription)
    {
        VerifyAlive();
        ArgumentNullException.ThrowIfNull(scene);
        targetDescription.Validate();
        if ((targetDescription.Usage & GpuTextureUsage.ColorAttachment) == 0)
        {
            throw new ArgumentException("The target requires color-attachment usage.", nameof(targetDescription));
        }
        return new(this, scene, targetDescription);
    }

    public void Dispose()
    {
        if (disposed) { return; }
        foreach (GpuRasterPipelineHandle pipeline in pipelines.Values)
        {
            backend.DestroyRasterPipeline(pipeline);
        }
        foreach (GpuRasterPipelineHandle pipeline in layerPipelines.Values)
        {
            backend.DestroyRasterPipeline(pipeline);
        }
        foreach (GpuRasterPipelineHandle pipeline in blurPipelines.Values)
        {
            backend.DestroyRasterPipeline(pipeline);
        }
        foreach (GpuRasterPipelineHandle pipeline in maskPipelines.Values)
        {
            backend.DestroyRasterPipeline(pipeline);
        }
        if (!pathPreparationPipeline.IsNull)
        {
            backend.DestroyComputePipeline(pathPreparationPipeline);
            pathPreparationPipeline = default;
        }
        pipelines.Clear();
        layerPipelines.Clear();
        blurPipelines.Clear();
        maskPipelines.Clear();
        if (!layerSampler.IsNull)
        {
            backend.DestroySampler(layerSampler);
            layerSampler = default;
        }
        distanceFieldAtlases.Clear();
        images.Clear();
        disposed = true;
    }

    internal RegisteredImage RequireImage(ImageId image)
    {
        VerifyAlive();
        return !image.IsNull && images.TryGetValue(image, out RegisteredImage value)
            ? value
            : throw new ArgumentException("Image is not registered with this renderer.", nameof(image));
    }

    internal ImageId RequireDistanceField(DistanceField field)
    {
        VerifyAlive();
        DistanceFieldAtlas atlas = field.Owner
            ?? throw new ArgumentException("Distance field cannot be null.", nameof(field));
        atlas.Require(field);
        if (!ReferenceEquals(atlas.Backend, backend))
        {
            throw new ArgumentException("Distance field belongs to another GPU backend.", nameof(field));
        }
        if (distanceFieldAtlases.TryGetValue(atlas, out ImageId image)) { return image; }
        image = RegisterImage(atlas.Texture, atlas.Description, atlas.Sampler);
        distanceFieldAtlases.Add(atlas, image);
        return image;
    }

    internal GpuRasterPipelineHandle GetPipeline(GpuFormat format, PreparedBatchKind kind)
    {
        VerifyAlive();
        if (pipelines.TryGetValue((format, kind), out GpuRasterPipelineHandle pipeline))
        {
            return pipeline;
        }

        (string vertex, string pixel) = kind switch
        {
            PreparedBatchKind.Primitive => ("primitiveVertex", "primitivePixel"),
            PreparedBatchKind.Image => ("imageVertex", "imagePixel"),
            PreparedBatchKind.Polygon => ("polygonVertex", "polygonPixel"),
            PreparedBatchKind.DistanceField => ("distanceFieldVertex", "distanceFieldPixel"),
            PreparedBatchKind.Path => ("pathVertex", "pathPixel"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        GpuShaderPackage package = kind == PreparedBatchKind.Path
            ? phaseThreeShaders ??= StandardShaders.LoadPhaseThree()
            : phaseOneShaders ??= StandardShaders.LoadPhaseOne();
        var description = new GpuRasterPipelineDescription([new(format)])
        {
            EmbeddedBlend = new(
                SourceColorFactor: GpuBlendFactor.One,
                DestinationColorFactor: GpuBlendFactor.OneMinusSourceAlpha,
                SourceAlphaFactor: GpuBlendFactor.One,
                DestinationAlphaFactor: GpuBlendFactor.OneMinusSourceAlpha),
        };
        pipeline = backend.CreateRasterPipeline(
            description,
            package,
            vertex,
            pixel,
            GpuShaderBindingConvention.AbiHash);
        pipelines.Add((format, kind), pipeline);
        return pipeline;
    }

    internal SamplerId GetLayerSampler()
    {
        VerifyAlive();
        if (!layerSampler.IsNull) { return layerSampler; }
        layerSampler = backend.CreateSampler(new(
            GpuSamplerFilter.Linear,
            GpuSamplerFilter.Linear,
            GpuSamplerAddressMode.ClampToEdge,
            GpuSamplerAddressMode.ClampToEdge));
        return layerSampler;
    }

    internal GpuRasterPipelineHandle GetLayerPipeline(GpuFormat format, BlendMode mode)
    {
        VerifyAlive();
        if (layerPipelines.TryGetValue((format, mode), out GpuRasterPipelineHandle pipeline))
        {
            return pipeline;
        }
        phaseFourShaders ??= StandardShaders.LoadPhaseFour();
        var description = new GpuRasterPipelineDescription([new(format)])
        {
            EmbeddedBlend = Blend(mode),
        };
        pipeline = backend.CreateRasterPipeline(
            description,
            phaseFourShaders,
            "layerVertex",
            "layerPixel",
            GpuShaderBindingConvention.AbiHash);
        layerPipelines.Add((format, mode), pipeline);
        return pipeline;
    }

    internal GpuRasterPipelineHandle GetBlurPipeline(GpuFormat format)
    {
        VerifyAlive();
        if (blurPipelines.TryGetValue(format, out GpuRasterPipelineHandle pipeline))
        {
            return pipeline;
        }
        phaseFourShaders ??= StandardShaders.LoadPhaseFour();
        pipeline = backend.CreateRasterPipeline(
            new GpuRasterPipelineDescription([new(format)]),
            phaseFourShaders,
            "layerVertex",
            "blurPixel",
            GpuShaderBindingConvention.AbiHash);
        blurPipelines.Add(format, pipeline);
        return pipeline;
    }

    internal GpuRasterPipelineHandle GetMaskPipeline(GpuFormat format)
    {
        VerifyAlive();
        if (maskPipelines.TryGetValue(format, out GpuRasterPipelineHandle pipeline))
        {
            return pipeline;
        }
        phaseFourShaders ??= StandardShaders.LoadPhaseFour();
        var description = new GpuRasterPipelineDescription([new(format)])
        {
            EmbeddedBlend = new(
                SourceColorFactor: GpuBlendFactor.Zero,
                DestinationColorFactor: GpuBlendFactor.SourceAlpha,
                SourceAlphaFactor: GpuBlendFactor.Zero,
                DestinationAlphaFactor: GpuBlendFactor.SourceAlpha),
        };
        pipeline = backend.CreateRasterPipeline(
            description,
            phaseFourShaders,
            "layerVertex",
            "maskPixel",
            GpuShaderBindingConvention.AbiHash);
        maskPipelines.Add(format, pipeline);
        return pipeline;
    }

    private static GpuBlendDescription Blend(BlendMode mode) => mode switch
    {
        BlendMode.SourceOver => new(
            SourceColorFactor: GpuBlendFactor.One,
            DestinationColorFactor: GpuBlendFactor.OneMinusSourceAlpha,
            SourceAlphaFactor: GpuBlendFactor.One,
            DestinationAlphaFactor: GpuBlendFactor.OneMinusSourceAlpha),
        BlendMode.Additive => new(
            SourceColorFactor: GpuBlendFactor.One,
            DestinationColorFactor: GpuBlendFactor.One,
            SourceAlphaFactor: GpuBlendFactor.One,
            DestinationAlphaFactor: GpuBlendFactor.One),
        BlendMode.Multiply => new(
            SourceColorFactor: GpuBlendFactor.DestinationColor,
            DestinationColorFactor: GpuBlendFactor.OneMinusSourceAlpha,
            SourceAlphaFactor: GpuBlendFactor.One,
            DestinationAlphaFactor: GpuBlendFactor.OneMinusSourceAlpha),
        BlendMode.Screen => new(
            SourceColorFactor: GpuBlendFactor.OneMinusDestinationColor,
            DestinationColorFactor: GpuBlendFactor.One,
            SourceAlphaFactor: GpuBlendFactor.One,
            DestinationAlphaFactor: GpuBlendFactor.OneMinusSourceAlpha),
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    private GpuComputePipelineHandle GetPathPreparationPipeline()
    {
        if (!pathPreparationPipeline.IsNull) { return pathPreparationPipeline; }
        phaseThreeShaders ??= StandardShaders.LoadPhaseThree();
        pathPreparationPipeline = backend.CreateComputePipeline(
            phaseThreeShaders,
            "preparePath",
            GpuShaderBindingConvention.AbiHash);
        return pathPreparationPipeline;
    }

    private void PreparePaths(
        OwnedBuffer input,
        OwnedBuffer output,
        IReadOnlyList<PathComputeJob> jobs)
    {
        var views = new List<GpuBufferView>(jobs.Count * 2);
        try
        {
            GpuCommandBuffer commands = backend.MainQueue.StartCommandRecording();
            for (int index = 0; index < jobs.Count; index++)
            {
                PathComputeJob job = jobs[index];
                GpuBufferView inputView = backend.CreateBufferView(
                    input.Buffer,
                    new(job.InputOffset, job.InputLength));
                GpuBufferView outputView = backend.CreateBufferView(
                    output.Buffer,
                    new(job.OutputOffset, job.OutputLength, GpuBufferViewAccess.ReadWrite));
                views.Add(inputView);
                views.Add(outputView);
                var resources = new GpuResourceTable(0, 0, 1, 0, 1);
                resources.SetBuffer(0, inputView.Id);
                resources.SetWritableBuffer(0, outputView.Id);
                commands
                    .SetComputePipeline(GetPathPreparationPipeline())
                    .SetComputeResourceTable(resources)
                    .Dispatch(1)
                    .Barrier(
                        GpuStage.ComputeShader,
                        index + 1 == jobs.Count
                            ? GpuStage.VertexShader | GpuStage.PixelShader
                            : GpuStage.ComputeShader,
                        GpuBarrierHazards.Descriptors);
            }

            using GpuSemaphore completion = backend.MainQueue.CreateSemaphore();
            backend.MainQueue.Submit([commands], completion, 1);
            backend.MainQueue.Wait(completion, 1);
        }
        finally
        {
            foreach (GpuBufferView view in views)
            {
                backend.DestroyBufferView(view);
            }
        }
    }

    private int GetImageIndex(
        ImageId image,
        List<PreparedImage> prepared,
        Dictionary<ImageId, int> indices)
    {
        if (indices.TryGetValue(image, out int index)) { return index; }
        RegisteredImage registered = RequireImage(image);
        index = prepared.Count;
        prepared.Add(new(image, registered.Texture, registered.Description, registered.Sampler));
        indices.Add(image, index);
        return index;
    }

    private PreparedLayer[] PrepareLayers(
        ReadOnlySpan<RecordedLayer> recorded,
        GpuTextureDescription target,
        List<PreparedImage> images,
        Dictionary<ImageId, int> imageIndices,
        out byte[] bytes)
    {
        if (recorded.IsEmpty)
        {
            bytes = [];
            return [];
        }

        int stride = checked(ShaderBufferOffsetAlignment * LayerParameterSlots);
        bytes = new byte[checked(recorded.Length * stride)];
        var prepared = new PreparedLayer[recorded.Length];
        for (int index = 0; index < recorded.Length; index++)
        {
            RecordedLayer layer = recorded[index];
            LayerOptions options = layer.Options;
            int maskImageIndex = options.Mask.IsNull
                ? -1
                : GetImageIndex(options.Mask, images, imageIndices);
            ulong baseOffset = checked((ulong)(index * stride));
            ulong mainOffset = baseOffset;
            ulong shadowOffset = checked(baseOffset + ShaderBufferOffsetAlignment);
            ulong horizontalOffset = checked(shadowOffset + ShaderBufferOffsetAlignment);
            ulong verticalOffset = checked(horizontalOffset + ShaderBufferOffsetAlignment);
            ulong shadowHorizontalOffset = checked(verticalOffset + ShaderBufferOffsetAlignment);
            ulong shadowVerticalOffset = checked(shadowHorizontalOffset + ShaderBufferOffsetAlignment);

            WriteLayerCommand(bytes, mainOffset, new()
            {
                Settings = new(options.Opacity, maskImageIndex >= 0 ? 1 : 0, 0, 0),
                Tint = Color.White.Premultiplied(),
                OffsetAndTargetSize = new(0, 0, target.Width, target.Height),
            });
            ShadowOptions shadow = options.Shadow ?? default;
            WriteLayerCommand(bytes, shadowOffset, new()
            {
                Settings = new(options.Opacity, maskImageIndex >= 0 ? 1 : 0, 1, 0),
                Tint = shadow.Color.Premultiplied(),
                OffsetAndTargetSize = new(shadow.Offset, target.Width, target.Height),
            });
            WriteLayerCommand(bytes, horizontalOffset, new()
            {
                Settings = new(options.BlurRadius, 0, 0, 0),
                OffsetAndTargetSize = new(0, 0, target.Width, target.Height),
                Direction = new(1, 0, 0, 0),
            });
            WriteLayerCommand(bytes, verticalOffset, new()
            {
                Settings = new(options.BlurRadius, 0, 0, 0),
                OffsetAndTargetSize = new(0, 0, target.Width, target.Height),
                Direction = new(0, 1, 0, 0),
            });
            WriteLayerCommand(bytes, shadowHorizontalOffset, new()
            {
                Settings = new(shadow.BlurRadius, 0, 0, 0),
                OffsetAndTargetSize = new(0, 0, target.Width, target.Height),
                Direction = new(1, 0, 0, 0),
            });
            WriteLayerCommand(bytes, shadowVerticalOffset, new()
            {
                Settings = new(shadow.BlurRadius, 0, 0, 0),
                OffsetAndTargetSize = new(0, 0, target.Width, target.Height),
                Direction = new(0, 1, 0, 0),
            });
            prepared[index] = new(
                layer.Id,
                layer.ParentId,
                layer.Sequence,
                options,
                maskImageIndex,
                mainOffset,
                shadowOffset,
                horizontalOffset,
                verticalOffset,
                shadowHorizontalOffset,
                shadowVerticalOffset);
        }
        return prepared;
    }

    private static void WriteLayerCommand(byte[] bytes, ulong offset, GpuLayerCommand command)
    {
        Span<byte> destination = bytes.AsSpan(checked((int)offset), GpuLayerCommand.Size);
        MemoryMarshal.Write(destination, in command);
    }

    internal static GpuDrawCommand CreateGpuCommand(
        RecordedCommand command,
        GpuTextureDescription target)
    {
        Matrix3x2 transform = command.Transform;
        CornerRadius radius = command.CornerRadius;
        var result = new GpuDrawCommand
        {
            Header = new((uint)command.Kind, 0, 0, 0),
            Bounds = new(command.Bounds.X, command.Bounds.Y, command.Bounds.Width, command.Bounds.Height),
            Color = command.Brush.Color.Premultiplied(),
            Parameters0 = command.Kind switch
            {
                DrawCommandKind.RoundedRectangle => new(
                    radius.TopLeft, radius.TopRight, radius.BottomRight, radius.BottomLeft),
                DrawCommandKind.Line => new(
                    command.LineStart.X,
                    command.LineStart.Y,
                    command.LineEnd.X,
                    command.LineEnd.Y),
                _ => default,
            },
            Parameters1 = command.Kind == DrawCommandKind.Line
                ? new(command.LineWidth, 0, 0, 0)
                : default,
            TextureRegion = command.Kind == DrawCommandKind.Image
                || command.Kind == DrawCommandKind.DistanceField
                ? new(command.Source.X, command.Source.Y, command.Source.Width, command.Source.Height)
                : default,
            Transform0 = new(transform.M11, transform.M12, transform.M21, transform.M22),
            Transform1 = new(transform.M31, transform.M32, target.Width, target.Height),
        };
        if (command.Kind == DrawCommandKind.DistanceField)
        {
            DistanceFieldEntry entry = command.DistanceField.Owner!.Require(command.DistanceField);
            result.Parameters0 = new(
                entry.DistanceRange,
                (float)entry.Encoding,
                entry.Region.Width,
                entry.Region.Height);
        }
        return result;
    }

    private static void AddBatch(
        List<PreparedBatch> batches,
        PreparedBatchKind kind,
        ulong offset,
        ulong length,
        Rect clip,
        int imageIndex,
        int layerId,
        int sequence)
    {
        if (batches.Count != 0)
        {
            PreparedBatch previous = batches[^1];
            if (previous.Kind == kind
                && previous.Clip == clip
                && previous.ImageIndex == imageIndex
                && previous.LayerId == layerId
                && previous.BufferOffset + previous.BufferLength == offset)
            {
                batches[^1] = previous with
                {
                    BufferLength = checked(previous.BufferLength + length),
                    DrawCount = checked(previous.DrawCount + 1),
                };
                return;
            }
        }
        batches.Add(new(kind, offset, length, 1, clip, imageIndex, layerId, sequence));
    }

    private static bool CanAppendBatch(
        List<PreparedBatch> batches,
        PreparedBatchKind kind,
        ulong offset,
        Rect clip,
        int imageIndex,
        int layerId)
    {
        if (batches.Count == 0) { return false; }
        PreparedBatch previous = batches[^1];
        return previous.Kind == kind
            && previous.Clip == clip
            && previous.ImageIndex == imageIndex
            && previous.LayerId == layerId
            && previous.BufferOffset + previous.BufferLength == offset;
    }

    private static void AddPolygon(
        RecordedCommand command,
        Rect clip,
        GpuTextureDescription target,
        ArrayBufferWriter<byte> bytes,
        List<PreparedBatch> batches,
        int sequence)
    {
        PolygonGeometry geometry = command.Geometry
            ?? throw new InvalidOperationException("Polygon command has no geometry.");
        int padding = (ShaderBufferOffsetAlignment - bytes.WrittenCount % ShaderBufferOffsetAlignment)
            % ShaderBufferOffsetAlignment;
        if (padding != 0)
        {
            bytes.GetSpan(padding)[..padding].Clear();
            bytes.Advance(padding);
        }
        int offset = bytes.WrittenCount;
        Matrix3x2 transform = command.Transform;
        var header = new GpuPolygonHeader
        {
            Color = command.Brush.Color.Premultiplied(),
            Transform0 = new(transform.M11, transform.M12, transform.M21, transform.M22),
            Transform1 = new(transform.M31, transform.M32, target.Width, target.Height),
        };
        Span<byte> headerDestination = bytes.GetSpan(GpuPolygonHeader.Size)[..GpuPolygonHeader.Size];
        MemoryMarshal.Write(headerDestination, in header);
        bytes.Advance(GpuPolygonHeader.Size);

        foreach (Vector2 vertex in geometry.Vertices.Span)
        {
            var packed = new Vector4(vertex, 0, 0);
            Span<byte> destination = bytes.GetSpan(16)[..16];
            MemoryMarshal.Write(destination, in packed);
            bytes.Advance(16);
        }
        int length = checked(bytes.WrittenCount - offset);
        batches.Add(new(
            PreparedBatchKind.Polygon,
            checked((ulong)offset),
            checked((ulong)length),
            checked((uint)geometry.Vertices.Length),
            clip,
            LayerId: command.LayerId,
            Sequence: sequence));
    }

    private static void AddPath(
        RecordedCommand command,
        Rect clip,
        GpuTextureDescription target,
        ArrayBufferWriter<byte> inputBytes,
        List<PreparedBatch> batches,
        List<PathComputeJob> jobs,
        ref ulong outputLength,
        int sequence)
    {
        int padding = (ShaderBufferOffsetAlignment - inputBytes.WrittenCount % ShaderBufferOffsetAlignment)
            % ShaderBufferOffsetAlignment;
        if (padding != 0)
        {
            inputBytes.GetSpan(padding)[..padding].Clear();
            inputBytes.Advance(padding);
        }
        int inputOffset = inputBytes.WrittenCount;
        PathBatchData data = PathBatchCompiler.Compile(command, target);
        data.InputBytes.CopyTo(inputBytes.GetSpan(data.InputBytes.Length));
        inputBytes.Advance(data.InputBytes.Length);
        ulong outputOffset = Align(outputLength, ShaderBufferOffsetAlignment);
        outputLength = checked(outputOffset + data.OutputLength);
        batches.Add(new(
            PreparedBatchKind.Path,
            outputOffset,
            data.OutputLength,
            1,
            clip,
            LayerId: command.LayerId,
            Sequence: sequence));
        jobs.Add(new(
            checked((ulong)inputOffset),
            checked((ulong)data.InputBytes.Length),
            outputOffset,
            data.OutputLength));
    }

    private static ulong Align(ulong value, ulong alignment)
        => checked((value + alignment - 1) & ~(alignment - 1));

    private void VerifyAlive() => ObjectDisposedException.ThrowIf(disposed, this);
}
