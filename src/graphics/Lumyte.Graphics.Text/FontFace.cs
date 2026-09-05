using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.TwoD;
using HbBlob = HarfBuzzSharp.Blob;
using HbBuffer = HarfBuzzSharp.Buffer;
using HbFace = HarfBuzzSharp.Face;
using HbFont = HarfBuzzSharp.Font;

namespace Lumyte.Graphics.Text;

/// <summary>
/// Owns an OpenType font face and provides size-independent HarfBuzz shaping.
/// </summary>
public sealed class FontFace : IDisposable
{
    private const uint MaximumColorLayerCount = 4_096;

    private readonly byte[] fontData;
    private readonly HbBlob blob;
    private readonly HbFace face;
    private readonly HbFont font;
    private readonly ConcurrentBag<HbBuffer> shapingBuffers = [];
    private readonly ConcurrentDictionary<uint, Lazy<GlyphOutline?>> outlineCache = [];
    private readonly ConcurrentDictionary<uint, Lazy<PathGeometry?>> pathCache = [];
    private readonly ConcurrentDictionary<uint, Lazy<ColorGlyph?>> colorGlyphCache = [];
    private readonly ConcurrentDictionary<ColorPaintGlyphKey, Lazy<ColorPaintGlyph?>> colorPaintGlyphCache = [];
    private readonly ConcurrentDictionary<ColorBitmapGlyphKey, Lazy<ColorBitmapGlyph?>> colorBitmapGlyphCache = [];
    private readonly ConcurrentDictionary<uint, Lazy<Color[]>> colorPaletteCache = [];
    private readonly ReaderWriterLockSlim lifetimeLock = new(LockRecursionPolicy.NoRecursion);
    private readonly TrueTypeOutlineReader? outlineReader;
    private readonly ReadOnlyCollection<FontVariation> variations;
    private bool disposed;

    /// <summary>Creates a face from OpenType font bytes.</summary>
    /// <param name="fontData">The complete TTF, OTF, or TTC data.</param>
    /// <param name="fontIndex">The zero-based face index inside a font collection.</param>
    /// <param name="variations">
    /// Optional immutable design-space variation settings. An axis may be specified at most once.
    /// </param>
    public FontFace(
        ReadOnlyMemory<byte> fontData,
        int fontIndex = 0,
        IEnumerable<FontVariation>? variations = null)
    {
        if (fontData.IsEmpty)
        {
            throw new ArgumentException("Font data must not be empty.", nameof(fontData));
        }
        if (fontIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontIndex));
        }

        this.fontData = fontData.ToArray();
        this.variations = CopyVariations(variations);
        FontIndex = fontIndex;

        GCHandle createdPin = default;
        HbBlob? createdBlob = null;
        HbFace? createdFace = null;
        HbFont? createdFont = null;
        try
        {
            createdPin = GCHandle.Alloc(this.fontData, GCHandleType.Pinned);
            createdBlob = new(
                createdPin.AddrOfPinnedObject(),
                this.fontData.Length,
                HarfBuzzSharp.MemoryMode.ReadOnly,
                () =>
                {
                    if (createdPin.IsAllocated)
                    {
                        createdPin.Free();
                    }
                });
            createdFace = new(createdBlob, fontIndex);
            createdFont = new(createdFace);
            ApplyVariations(createdFont, this.variations);

            int unitsPerEm = createdFace.UnitsPerEm;
            if (unitsPerEm <= 0)
            {
                throw new NotSupportedException("The font face does not define a valid units-per-em value.");
            }
            createdFont.SetScale(unitsPerEm, unitsPerEm);
            if (!createdFont.TryGetHorizontalFontExtents(out HarfBuzzSharp.FontExtents extents))
            {
                throw new NotSupportedException("The font face does not provide horizontal font extents.");
            }

            UnitsPerEm = unitsPerEm;
            Ascent = extents.Ascender;
            Descent = extents.Descender;
            HasColorLayerGlyphs = createdFace.HasColorLayers;
            HasColorPaintGlyphs = HarfBuzzNative.HasColorPaint(createdFace.Handle) != 0;
            HasColorBitmapGlyphs = createdFace.HasColorPng;
            ColorPaletteCount = checked((uint)createdFace.PaletteCount);
            outlineReader = TrueTypeOutlineReader.TryCreate(createdFace);
        }
        catch
        {
            createdFont?.Dispose();
            createdFace?.Dispose();
            createdBlob?.Dispose();
            if (createdPin.IsAllocated)
            {
                createdPin.Free();
            }
            throw;
        }

        blob = createdBlob;
        face = createdFace;
        font = createdFont;
    }

    /// <summary>Loads a font face from a TTF, OTF, or TTC file.</summary>
    public static FontFace Load(
        string path,
        int fontIndex = 0,
        IEnumerable<FontVariation>? variations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new(File.ReadAllBytes(path), fontIndex, variations);
    }

    /// <summary>The immutable OpenType bytes used to create the face.</summary>
    public ReadOnlyMemory<byte> FontData => fontData;

    /// <summary>The zero-based face index inside a font collection.</summary>
    public int FontIndex { get; }

    /// <summary>The immutable design-space variation settings applied to this face.</summary>
    public IReadOnlyList<FontVariation> Variations => variations;

    /// <summary>The number of font units in one em.</summary>
    public int UnitsPerEm { get; }

    /// <summary>The horizontal ascender in font units.</summary>
    public float Ascent { get; }

    /// <summary>The horizontal descender in font units, normally negative.</summary>
    public float Descent { get; }

    /// <summary>Whether the face contains a supported vector or bitmap color-glyph table.</summary>
    public bool HasColorGlyphs => HasColorLayerGlyphs || HasColorPaintGlyphs || HasColorBitmapGlyphs;

    /// <summary>Whether the face contains layered COLRv0 vector glyphs.</summary>
    public bool HasColorLayerGlyphs { get; }

    /// <summary>Whether the face contains a COLRv1 vector paint graph.</summary>
    public bool HasColorPaintGlyphs { get; }

    /// <summary>Whether the face contains PNG glyphs in an OpenType CBDT or sbix table.</summary>
    public bool HasColorBitmapGlyphs { get; }

    /// <summary>The number of CPAL palettes supplied by this face.</summary>
    public uint ColorPaletteCount { get; }

    /// <summary>Returns one CPAL palette converted from sRGB to the renderer's linear color space.</summary>
    public ReadOnlyMemory<Color> GetColorPalette(uint paletteIndex)
    {
        lifetimeLock.EnterReadLock();
        try
        {
            ThrowIfDisposed();
            if (paletteIndex >= ColorPaletteCount)
            {
                throw new ArgumentOutOfRangeException(nameof(paletteIndex));
            }

            return colorPaletteCache.GetOrAdd(
                paletteIndex,
                index => new(
                    () => CreateColorPalette(index),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
        finally
        {
            lifetimeLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Shapes text with HarfBuzz. Glyph advances and offsets remain size-independent font units,
    /// and every cluster is an index into the input string's UTF-16 code units.
    /// </summary>
    public ShapedText Shape(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        lifetimeLock.EnterReadLock();
        try
        {
            ThrowIfDisposed();
            if (text.Length == 0)
            {
                return new(this, [], Vector2.Zero);
            }

            HbBuffer buffer = RentShapingBuffer();
            try
            {
                buffer.ClearContents();
                buffer.AddUtf16(text);
                buffer.GuessSegmentProperties();
                font.Shape(buffer);

                ReadOnlySpan<HarfBuzzSharp.GlyphInfo> information = buffer.GetGlyphInfoSpan();
                ReadOnlySpan<HarfBuzzSharp.GlyphPosition> positions = buffer.GetGlyphPositionSpan();
                if (information.Length != positions.Length)
                {
                    throw new InvalidOperationException("HarfBuzz returned mismatched glyph information and position arrays.");
                }

                var glyphs = new ShapedGlyph[information.Length];
                Vector2 advance = Vector2.Zero;
                for (int index = 0; index < glyphs.Length; index++)
                {
                    HarfBuzzSharp.GlyphPosition position = positions[index];
                    glyphs[index] = new(
                        information[index].Codepoint,
                        checked((int)information[index].Cluster),
                        position.XAdvance,
                        position.YAdvance,
                        position.XOffset,
                        position.YOffset);
                    advance += new Vector2(position.XAdvance, position.YAdvance);
                }
                return new(this, glyphs, advance);
            }
            finally
            {
                shapingBuffers.Add(buffer);
            }
        }
        finally
        {
            lifetimeLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Measures one shaped line in pixels at the requested em size. The returned Y component is
    /// the face's ascender-to-descender height; line gap and multi-line layout are not included.
    /// </summary>
    public Vector2 Measure(string text, float fontSize)
    {
        if (!float.IsFinite(fontSize) || fontSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize));
        }

        ShapedText shaped = Shape(text);
        float scale = fontSize / UnitsPerEm;
        return new(shaped.XAdvance * scale, (Ascent - Descent) * scale);
    }

    /// <summary>Returns whether this face maps a Unicode scalar to a non-notdef glyph.</summary>
    public bool HasGlyph(int unicodeScalar)
        => TryGetGlyph(unicodeScalar, out _);

    /// <summary>Maps a Unicode scalar to a glyph index.</summary>
    public bool TryGetGlyph(int unicodeScalar, out uint glyphId)
    {
        if (unicodeScalar < 0 || unicodeScalar > 0x10FFFF)
        {
            glyphId = 0;
            return false;
        }

        lifetimeLock.EnterReadLock();
        try
        {
            ThrowIfDisposed();
            return font.TryGetGlyph(unicodeScalar, out glyphId) && glyphId != 0;
        }
        finally
        {
            lifetimeLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Resolves a glyph as a reusable path in font units. The returned path uses y-down coordinates
    /// with the baseline at Y=0, so Path, SDF, and MSDF routes can share the same positive scale.
    /// TrueType, CFF, and CFF2 outlines share this path; glyphs without an outline return
    /// <see langword="false"/> without affecting shaping.
    /// </summary>
    public bool TryGetGlyphPath(
        uint glyphId,
        [NotNullWhen(true)] out PathGeometry? path)
    {
        lifetimeLock.EnterReadLock();
        try
        {
            ThrowIfDisposed();
            path = GetPath(glyphId);
            return path is not null;
        }
        finally
        {
            lifetimeLock.ExitReadLock();
        }
    }

    internal bool TryGetGlyphOutline(
        uint glyphId,
        [NotNullWhen(true)] out GlyphOutline? outline)
    {
        lifetimeLock.EnterReadLock();
        try
        {
            ThrowIfDisposed();
            GlyphOutline? result = GetOutline(glyphId);
            if (result is null)
            {
                outline = null;
                return false;
            }

            outline = result;
            return true;
        }
        finally
        {
            lifetimeLock.ExitReadLock();
        }
    }

    internal bool TryGetColorGlyph(
        uint glyphId,
        [NotNullWhen(true)] out ColorGlyph? colorGlyph)
    {
        lifetimeLock.EnterReadLock();
        try
        {
            ThrowIfDisposed();
            colorGlyph = colorGlyphCache.GetOrAdd(
                glyphId,
                id => new(
                    () => CreateColorGlyph(id),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            return colorGlyph is not null;
        }
        finally
        {
            lifetimeLock.ExitReadLock();
        }
    }

    internal bool TryGetColorPaintGlyph(
        uint glyphId,
        uint paletteIndex,
        [NotNullWhen(true)] out ColorPaintGlyph? colorGlyph)
    {
        uint paletteLimit = Math.Max(ColorPaletteCount, 1u);
        if (paletteIndex >= paletteLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(paletteIndex));
        }

        lifetimeLock.EnterReadLock();
        try
        {
            ThrowIfDisposed();
            if (!HasColorPaintGlyphs)
            {
                colorGlyph = null;
                return false;
            }

            var key = new ColorPaintGlyphKey(glyphId, paletteIndex);
            colorGlyph = colorPaintGlyphCache.GetOrAdd(
                key,
                value => new(
                    () => CreateColorPaintGlyph(value.GlyphId, value.PaletteIndex),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            return colorGlyph is not null;
        }
        finally
        {
            lifetimeLock.ExitReadLock();
        }
    }

    internal bool TryGetColorBitmapGlyph(
        uint glyphId,
        uint pixelsPerEm,
        [NotNullWhen(true)] out ColorBitmapGlyph? colorGlyph)
    {
        if (pixelsPerEm == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerEm));
        }

        lifetimeLock.EnterReadLock();
        try
        {
            ThrowIfDisposed();
            if (!HasColorBitmapGlyphs)
            {
                colorGlyph = null;
                return false;
            }

            var key = new ColorBitmapGlyphKey(glyphId, pixelsPerEm);
            colorGlyph = colorBitmapGlyphCache.GetOrAdd(
                key,
                value => new(
                    () => CreateColorBitmapGlyph(value.GlyphId, value.PixelsPerEm),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            return colorGlyph is not null;
        }
        finally
        {
            lifetimeLock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        lifetimeLock.EnterWriteLock();
        try
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            outlineCache.Clear();
            pathCache.Clear();
            colorGlyphCache.Clear();
            colorPaintGlyphCache.Clear();
            colorBitmapGlyphCache.Clear();
            colorPaletteCache.Clear();
            while (shapingBuffers.TryTake(out HbBuffer? buffer))
            {
                buffer.Dispose();
            }
            font.Dispose();
            face.Dispose();
            blob.Dispose();
        }
        finally
        {
            lifetimeLock.ExitWriteLock();
        }
    }

    private HbBuffer RentShapingBuffer()
        => shapingBuffers.TryTake(out HbBuffer? buffer) ? buffer : new();

    private GlyphOutline? GetOutline(uint glyphId)
    {
        // The direct glyf reader does not resolve gvar deltas. Returning no pre-tessellated
        // outline makes the polygon route fall back to the HarfBuzz-resolved path instead.
        if (outlineReader is null || variations.Count != 0)
        {
            return null;
        }

        return outlineCache.GetOrAdd(
            glyphId,
            id => new(
                () => outlineReader.TryRead(id, out GlyphOutline? outline) ? outline : null,
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private PathGeometry? CreatePath(uint glyphId)
        => HarfBuzzOutlineReader.TryRead(font, glyphId, out PathGeometry? path) ? path : null;

    private PathGeometry? GetPath(uint glyphId)
        => pathCache.GetOrAdd(
            glyphId,
            id => new(
                () => CreatePath(id),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    private Color[] CreateColorPalette(uint paletteIndex)
    {
        HarfBuzzSharp.HBColor[] colors = face.GetPaletteColors(checked((int)paletteIndex));
        return colors.Select(static color => Color.FromSrgb(
            color.Red / 255f,
            color.Green / 255f,
            color.Blue / 255f,
            color.Alpha / 255f)).ToArray();
    }

    private unsafe ColorGlyph? CreateColorGlyph(uint glyphId)
    {
        if (!HasColorLayerGlyphs || ColorPaletteCount == 0)
        {
            return null;
        }

        uint layerCount = 0;
        uint total = HarfBuzzNative.GetColorLayers(face.Handle, glyphId, 0, &layerCount, null);
        if (total == 0 || total > MaximumColorLayerCount)
        {
            return null;
        }

        var records = new HarfBuzzColorLayer[checked((int)total)];
        layerCount = total;
        fixed (HarfBuzzColorLayer* recordsPointer = records)
        {
            uint reportedTotal = HarfBuzzNative.GetColorLayers(
                face.Handle,
                glyphId,
                0,
                &layerCount,
                recordsPointer);
            if (reportedTotal != total || layerCount != total)
            {
                return null;
            }
        }

        var layers = new ColorGlyphLayer[records.Length];
        for (int index = 0; index < layers.Length; index++)
        {
            HarfBuzzColorLayer record = records[index];
            PathGeometry? path = GetPath(record.GlyphId);
            if (path is null)
            {
                return null;
            }
            layers[index] = new(record.GlyphId, record.ColorIndex, path);
        }
        return new(layers);
    }

    private ColorPaintGlyph? CreateColorPaintGlyph(uint glyphId, uint paletteIndex)
    {
        try
        {
            try
            {
                return HarfBuzzPaintReader.TryRead(
                    font.Handle,
                    glyphId,
                    paletteIndex,
                    out ColorPaintGlyph? colorGlyph)
                    ? colorGlyph
                    : null;
            }
            catch (Exception exception) when (ColorPaintFailure.IsRecoverable(exception))
            {
                // A single malformed or forward-version paint graph must not poison this
                // Lazy cache entry or prevent the caller from trying another glyph route.
                return null;
            }
        }
        finally
        {
            GC.KeepAlive(font);
        }
    }

    private unsafe ColorBitmapGlyph? CreateColorBitmapGlyph(uint glyphId, uint pixelsPerEm)
    {
        using var bitmapFont = new HbFont(face);
        ApplyVariations(bitmapFont, variations);
        bitmapFont.SetScale(UnitsPerEm, UnitsPerEm);
        HarfBuzzNative.SetPixelsPerEm(bitmapFont.Handle, pixelsPerEm, pixelsPerEm);

        nint pngBlob = HarfBuzzNative.ReferenceColorPng(bitmapFont.Handle, glyphId);
        if (pngBlob == 0)
        {
            return null;
        }
        try
        {
            uint pngLength = 0;
            byte* pngData = HarfBuzzNative.GetBlobData(pngBlob, &pngLength);
            if (pngData is null || pngLength == 0 || pngLength > int.MaxValue)
            {
                return null;
            }
            if (!PngDecoder.TryDecode(
                    new ReadOnlySpan<byte>(pngData, checked((int)pngLength)),
                    out PngImage image)
                || !bitmapFont.TryGetGlyphExtents(glyphId, out HarfBuzzSharp.GlyphExtents extents))
            {
                return null;
            }

            float left = MathF.Min(extents.XBearing, extents.XBearing + extents.Width);
            float right = MathF.Max(extents.XBearing, extents.XBearing + extents.Width);
            float top = MathF.Min(-extents.YBearing, -(extents.YBearing + extents.Height));
            float bottom = MathF.Max(-extents.YBearing, -(extents.YBearing + extents.Height));
            if (right <= left || bottom <= top)
            {
                return null;
            }
            return new(
                image.Width,
                image.Height,
                image.Pixels,
                new(left, top, right - left, bottom - top));
        }
        finally
        {
            HarfBuzzNative.DestroyBlob(pngBlob);
        }
    }

    private readonly record struct ColorBitmapGlyphKey(uint GlyphId, uint PixelsPerEm);

    private readonly record struct ColorPaintGlyphKey(uint GlyphId, uint PaletteIndex);

    private static ReadOnlyCollection<FontVariation> CopyVariations(
        IEnumerable<FontVariation>? variations)
    {
        FontVariation[] copy = variations?.ToArray() ?? [];
        var tags = new HashSet<uint>();
        foreach (FontVariation variation in copy)
        {
            uint tag = variation.ToOpenTypeTag(nameof(variations));
            if (!tags.Add(tag))
            {
                throw new ArgumentException(
                    $"The OpenType variation axis '{variation.Tag}' is specified more than once.",
                    nameof(variations));
            }
        }

        return Array.AsReadOnly(copy);
    }

    private static void ApplyVariations(
        HbFont target,
        IReadOnlyList<FontVariation> settings)
    {
        if (settings.Count == 0)
        {
            return;
        }

        var native = new HarfBuzzSharp.Variation[settings.Count];
        for (int index = 0; index < native.Length; index++)
        {
            FontVariation setting = settings[index];
            native[index] = new()
            {
                Tag = setting.ToOpenTypeTag(nameof(settings)),
                Value = setting.Value,
            };
        }
        target.SetVariations(native);
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(disposed, this);
}
