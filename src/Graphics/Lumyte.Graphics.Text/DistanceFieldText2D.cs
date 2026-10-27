using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text;

/// <summary>Owns a reusable MSDF/MTSDF text atlas and its upload staging, without submitting GPU work.</summary>
public sealed class DistanceFieldText2D : IDisposable
{
    private readonly IGraphicsTexture _texture;
    private readonly IGraphicsTextureView _view;
    private readonly IGraphicsBuffer<byte> _upload;
    private readonly uint _pitch;
    private readonly IReadOnlyList<Entry> _entries;
    private bool _uploaded;
    private bool _disposed;

    private DistanceFieldText2D(IGraphicsTexture texture, IGraphicsTextureView view, IGraphicsBuffer<byte> upload, uint pitch, IReadOnlyList<Entry> entries)
    {
        _texture = texture;
        _view = view;
        _upload = upload;
        _pitch = pitch;
        _entries = entries;
    }

    /// <summary>Gets the borrowed atlas view; keep this owner alive through GPU completion.</summary>
    public IGraphicsTextureView View => _view;

    /// <summary>Generates and packs distance fields, allocates GPU storage and prepares upload staging.</summary>
    /// <param name="device">The borrowed graphics device.</param>
    /// <param name="layout">The shaped outline snapshot, with per-outline paints.</param>
    /// <param name="pixelsPerUnit">Positive atlas texels per logical unit.</param>
    /// <param name="distanceRange">Total encoded distance span in atlas texels.</param>
    /// <param name="mode">MSDF or MTSDF generation and sampling.</param>
    /// <param name="maximumAtlasDimension">Maximum atlas side, also limited by the device.</param>
    /// <param name="cancellationToken">Cancels generation between outlines and upload preparation.</param>
    /// <returns>An owner whose upload must be explicitly recorded before sampling.</returns>
    public static async ValueTask<DistanceFieldText2D> CreateAsync(
        IGraphicDevice device,
        TextLayout2D layout,
        float pixelsPerUnit = 1,
        float distanceRange = 8,
        DistanceFieldMode2D mode = DistanceFieldMode2D.Mtsdf,
        int maximumAtlasDimension = 2048,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumAtlasDimension);
        if (!float.IsFinite(pixelsPerUnit) || pixelsPerUnit <= 0 || !float.IsFinite(distanceRange) || distanceRange <= 0 || !Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit), "A positive scale, range and valid mode are required.");
        }

        int limit = (int)Math.Min(Math.Min((uint)maximumAtlasDimension, device.Caps.MaxTextureDimension2D), 4096u);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        var fields = new List<(DistanceFieldImage2D Image, PaintGraph2D Paint)>();
        foreach (TextGeometry2D geometry in layout.Geometry)
        {
            cancellationToken.ThrowIfCancellationRequested();
            fields.Add((DistanceFieldGenerator2D.Generate(geometry.Path, pixelsPerUnit, distanceRange, mode, limit), geometry.Paint));
        }

        int width = 1;
        while (width < limit && fields.Any(field => field.Image.Width > width))
        {
            width = Math.Min(checked(width * 2), limit);
        }

        List<(int X, int Y)> positions;
        int height;
        while (!TryPack(fields, width, limit, out positions, out height))
        {
            if (width == limit)
            {
                throw new NotSupportedException("The text exceeds the atlas size budget; split the layout or increase maximumAtlasDimension.");
            }

            width = Math.Min(checked(width * 2), limit);
        }

        uint alignment = device.GetTextureCopyLayout(TextureFormat.Rgba8Unorm).BytesPerRowAlignment;
        uint pitch = checked((((uint)width * 4) + alignment - 1) / alignment * alignment);
        byte[] pixels = new byte[checked((int)pitch * height)];
        IGraphicsTexture? texture = null;
        IGraphicsTextureView? view = null;
        IGraphicsBuffer<byte>? upload = null;
        try
        {
            texture = device.CreateTexture(new TextureDesc
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = TextureFormat.Rgba8Unorm,
                Usage = TextureUsage.Sampled | TextureUsage.CopyDestination,
            });
            view = texture.CreateView();
            var entries = new List<Entry>();
            for (int i = 0; i < fields.Count; i++)
            {
                DistanceFieldImage2D image = fields[i].Image;
                (int x, int y) = positions[i];
                for (int row = 0; row < image.Height; row++)
                {
                    image.Pixels.Span.Slice(row * image.Width * 4, image.Width * 4).CopyTo(pixels.AsSpan(checked(((y + row) * (int)pitch) + (x * 4))));
                }

                var pattern = new ImagePattern2D
                {
                    View = view,
                    Source = new Rect2D(x, y, image.Width, image.Height),
                    PatternToLocal = Matrix3x2.CreateScale(1 / image.PixelsPerUnit) * Matrix3x2.CreateTranslation(image.PlaneBounds.X, image.PlaneBounds.Y),
                    TileX = ImageTileMode2D.Decal,
                    TileY = ImageTileMode2D.Decal,
                };
                entries.Add(new Entry(image.PlaneBounds, PaintGraph2D.DistanceField(pattern, image.DistanceRange, fields[i].Paint, image.Mode)));
            }

            upload = device.CreateBuffer<byte>(new BufferDesc<byte> { Count = (ulong)pixels.Length, Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });
            await upload.MapAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                upload.CopyFrom(pixels);
            }
            finally
            {
                upload.Unmap();
            }

            var orderedEntries = new List<Entry>();
            int outline = 0;
            foreach (TextDrawing2D drawing in layout.Drawings)
            {
                if (drawing is TextGeometry2D)
                {
                    orderedEntries.Add(entries[outline++]);
                }
                else if (drawing is TextPaintGeometry2D vector)
                {
                    orderedEntries.Add(new Entry(vector.Bounds, vector.Paint));
                }
                else
                {
                    throw new NotSupportedException("Distance-field text accepts outlines and vector paint graphs.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new DistanceFieldText2D(texture, view, upload, pitch, orderedEntries);
        }
        catch
        {
            upload?.Dispose();
            view?.Dispose();
            texture?.Dispose();
            throw;
        }
    }

    /// <summary>Records the atlas upload exactly once, ending in Sampled state. Submit and wait are caller responsibilities.</summary>
    /// <param name="commands">Same-device recording command buffer outside a pass; discard it if recording fails.</param>
    public void RecordUpload(IGraphicsCommandBuffer commands)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(commands);
        if (_uploaded || commands.State != CommandBufferState.Recording)
        {
            throw new InvalidOperationException("The atlas upload requires a fresh recording and can only be recorded once.");
        }

        _uploaded = true;
        var copyWrite = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyWrite);
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = _upload.SliceElements(0, _upload.Count), Before = new(PipelineStage.Host, ResourceAccess.HostWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.Barrier(new TextureBarrierDesc { Texture = _texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.CopyDestination, Before = default, After = copyWrite });
        commands.CopyBufferToTexture(new() { Buffer = _upload.SliceElements(0, _upload.Count), BytesPerRow = _pitch, RowsPerImage = _texture.Height }, new() { Texture = _texture, Width = _texture.Width, Height = _texture.Height });
        commands.Barrier(new TextureBarrierDesc { Texture = _texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.CopyDestination, AfterState = TextureState.Sampled, Before = copyWrite, After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead) });
    }

    /// <summary>Records reusable atlas-backed text; upload must execute before the resulting draws sample the atlas.</summary>
    /// <param name="canvas">The actively recording canvas.</param>
    /// <param name="origin">Additional local translation.</param>
    public void Draw(DrawingCanvas2D canvas, Vector2 origin = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(canvas);
        if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(origin));
        }

        canvas.Save();
        try
        {
            canvas.Translate(origin);
            foreach (Entry entry in _entries)
            {
                canvas.FillRect(entry.Bounds, entry.Paint);
            }
        }
        finally
        {
            canvas.Restore();
        }
    }

    /// <summary>Releases all atlas resources; call only after every referencing GPU operation completes.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _upload.Dispose();
        _view.Dispose();
        _texture.Dispose();
    }

    private static bool TryPack(List<(DistanceFieldImage2D Image, PaintGraph2D Paint)> fields, int width, int limit, out List<(int X, int Y)> positions, out int height)
    {
        positions = [];
        int x = 0;
        int y = 0;
        int rowHeight = 0;
        foreach ((DistanceFieldImage2D image, _) in fields)
        {
            if (x + image.Width > width)
            {
                x = 0;
                y += rowHeight;
                rowHeight = 0;
            }

            if (image.Width > width || y + image.Height > limit)
            {
                height = 0;
                return false;
            }

            positions.Add((x, y));
            x += image.Width;
            rowHeight = Math.Max(rowHeight, image.Height);
        }

        height = Math.Max(1, y + rowHeight);
        return true;
    }

    private sealed record Entry(Rect2D Bounds, PaintGraph2D Paint);
}
