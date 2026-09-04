using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;

namespace Lumyte.Graphics.TwoD;

/// <summary>Owns 2D pipelines and prepares immutable display lists for one GPU backend.</summary>
public sealed class Renderer : IDisposable
{
    private const int ShaderBufferOffsetAlignment = 256;

    private readonly IGpuBackend backend;
    private readonly Dictionary<ImageId, RegisteredImage> images = [];
    private readonly Dictionary<DistanceFieldAtlas, ImageId> distanceFieldAtlases = [];
    private readonly Dictionary<(GpuFormat Format, PreparedBatchKind Kind), GpuRasterPipelineHandle> pipelines = [];
    private GpuShaderPackage? shaders;
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
        var batches = new List<PreparedBatch>();
        var preparedImages = new List<PreparedImage>();
        var imageIndices = new Dictionary<ImageId, int>();
        var targetBounds = new Rect(0, 0, targetDescription.Width, targetDescription.Height);
        int acceptedCommands = 0;

        foreach (RecordedCommand command in displayList.Commands)
        {
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
                AddPolygon(command, clip.Value, targetDescription, polygonBytes, batches);
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
                if (!CanAppendBatch(batches, kind, commandOffset, clip.Value, imageIndex))
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
                    imageIndex);
            }
            acceptedCommands++;
        }

        OwnedBuffer? primitiveBuffer = null;
        OwnedBuffer? polygonBuffer = null;
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
            return new(
                this,
                targetDescription,
                acceptedCommands,
                batches.ToArray(),
                preparedImages.ToArray(),
                primitiveBuffer,
                polygonBuffer);
        }
        catch
        {
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
        pipelines.Clear();
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
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        shaders ??= StandardShaders.LoadPhaseOne();
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
            shaders,
            vertex,
            pixel,
            GpuShaderBindingConvention.AbiHash);
        pipelines.Add((format, kind), pipeline);
        return pipeline;
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
        int imageIndex)
    {
        if (batches.Count != 0)
        {
            PreparedBatch previous = batches[^1];
            if (previous.Kind == kind
                && previous.Clip == clip
                && previous.ImageIndex == imageIndex
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
        batches.Add(new(kind, offset, length, 1, clip, imageIndex));
    }

    private static bool CanAppendBatch(
        List<PreparedBatch> batches,
        PreparedBatchKind kind,
        ulong offset,
        Rect clip,
        int imageIndex)
    {
        if (batches.Count == 0) { return false; }
        PreparedBatch previous = batches[^1];
        return previous.Kind == kind
            && previous.Clip == clip
            && previous.ImageIndex == imageIndex
            && previous.BufferOffset + previous.BufferLength == offset;
    }

    private static void AddPolygon(
        RecordedCommand command,
        Rect clip,
        GpuTextureDescription target,
        ArrayBufferWriter<byte> bytes,
        List<PreparedBatch> batches)
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
            clip));
    }

    private void VerifyAlive() => ObjectDisposedException.ThrowIf(disposed, this);
}
