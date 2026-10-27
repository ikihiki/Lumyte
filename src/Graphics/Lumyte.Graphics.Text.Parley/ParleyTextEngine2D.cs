using System.Text;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Text.Parley.Interop;

namespace Lumyte.Graphics.Text.Parley;

/// <summary>Owns explicitly registered fonts and Parley caches while producing independent vector layouts.</summary>
/// <remarks>Calls on one engine are serialized. Completed layouts retain no native fonts or engine resources.</remarks>
public sealed unsafe class ParleyTextEngine2D : IDisposable
{
    private const int MaximumFontBytes = 64 * 1024 * 1024;
    private readonly object _sync = new();
    private readonly Dictionary<string, uint> _families = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, string> _fontNames = [];
    private readonly ParleyEngineHandle _handle;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="ParleyTextEngine2D"/> class without reading operating-system fonts.</summary>
    public ParleyTextEngine2D()
    {
        if (NativeMethods.lp_abi_version() != 1)
        {
            throw new NotSupportedException("The loaded Parley native library has an incompatible ABI.");
        }

        LpEngine* engine = null;
        ParleyNativeStatus.Check(NativeMethods.lp_engine_new(&engine));
        if (engine is null)
        {
            throw new InvalidOperationException("The native text engine returned an empty owner.");
        }

        _handle = new ParleyEngineHandle((nint)engine);
    }

    /// <summary>Copies one TrueType or OpenType face from the stream's current position into the engine.</summary>
    /// <param name="stream">A readable font stream which remains owned by the caller.</param>
    /// <returns>The family name to use in paragraph and fallback settings.</returns>
    public string AddFont(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[81920];
        int count;
        while ((count = stream.Read(chunk)) != 0)
        {
            if (buffer.Length + count > MaximumFontBytes)
            {
                throw new ArgumentException("Font data exceeds the 64 MiB registration limit.", nameof(stream));
            }

            buffer.Write(chunk, 0, count);
        }

        return AddFont(buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)));
    }

    /// <summary>Copies one TrueType or OpenType face into the engine.</summary>
    /// <param name="fontData">Complete face bytes, copied before returning.</param>
    /// <returns>The registered family name.</returns>
    public string AddFont(ReadOnlySpan<byte> fontData)
    {
        if (fontData.Length > MaximumFontBytes)
        {
            throw new ArgumentException("Font data exceeds the 64 MiB registration limit.", nameof(fontData));
        }

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            bool addedReference = false;
            _handle.DangerousAddRef(ref addedReference);
            try
            {
                var engine = (LpEngine*)_handle.DangerousGetHandle();
                uint id = 0;
                fixed (byte* bytes = fontData)
                {
                    ParleyNativeStatus.Check(NativeMethods.lp_font_add(engine, bytes, (nuint)fontData.Length, &id));
                }

                byte* name = null;
                nuint length = 0;
                ParleyNativeStatus.Check(NativeMethods.lp_font_name(engine, id, &name, &length));
                if (length > int.MaxValue || name is null)
                {
                    throw new InvalidDataException("The native font family name is invalid.");
                }

                string family = Encoding.UTF8.GetString(new ReadOnlySpan<byte>(name, (int)length));
                _families[family] = id;
                _fontNames[id] = family;
                return family;
            }
            finally
            {
                if (addedReference)
                {
                    _handle.DangerousRelease();
                }
            }
        }
    }

    /// <summary>Shapes a paragraph and copies its outlines and color paints into a reusable 2D snapshot.</summary>
    /// <param name="text">Plain Unicode text; markup is not interpreted.</param>
    /// <param name="options">Paragraph settings; null selects defaults and the first registered font.</param>
    /// <param name="paint">Paint in paragraph coordinates, also used for COLR currentColor; null selects black.</param>
    /// <returns>A snapshot usable after disposing this engine.</returns>
    public TextLayout2D CreateLayout(string text, ParleyTextOptions2D? options = null, PaintGraph2D? paint = null)
        => CreateDetailedLayout(text, options, paint).Layout;

    /// <summary>Shapes a paragraph while retaining font, glyph and source-cluster information.</summary>
    /// <param name="text">Plain Unicode text; malformed UTF-16 is rejected.</param>
    /// <param name="options">Paragraph settings, copied during this call.</param>
    /// <param name="paint">Paint in paragraph coordinates; null selects black.</param>
    /// <returns>The independent vector layout and positioned glyph metadata.</returns>
    public ParleyTextLayout2D CreateDetailedLayout(string text, ParleyTextOptions2D? options = null, PaintGraph2D? paint = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        options ??= new ParleyTextOptions2D();
        options = options with
        {
            FallbackFontFamilies = options.FallbackFontFamilies?.ToArray() ?? throw new ArgumentNullException(nameof(options.FallbackFontFamilies)),
            FontFeatures = options.FontFeatures?.ToArray() ?? throw new ArgumentNullException(nameof(options.FontFeatures)),
            FontVariations = options.FontVariations?.ToArray() ?? throw new ArgumentNullException(nameof(options.FontVariations)),
            TextRuns = options.TextRuns?.ToArray() ?? throw new ArgumentNullException(nameof(options.TextRuns)),
        };
        var source = new Utf8Text2D(text);
        ValidateOptions(options);
        paint ??= PaintGraph2D.Solid(new Color2D(0, 0, 0, 1));

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_families.Count == 0)
            {
                throw new InvalidOperationException("Register at least one font before creating a layout.");
            }

            uint fontId = options.FontFamily is null ? _fontNames.First().Key : FindFont(options.FontFamily);
            uint[] fallbacks = options.FallbackFontFamilies.Select(FindFont).ToArray();
            LpFeature[] features = options.FontFeatures.Select(feature => new LpFeature { tag = Utf8Text2D.FontTag(feature.Tag), value = feature.Value }).ToArray();
            LpVariation[] variations = options.FontVariations.Select(variation => new LpVariation { tag = Utf8Text2D.FontTag(variation.Tag), value = variation.Value }).ToArray();
            var paints = new List<PaintGraph2D> { paint };
            LpSpan[] spans = options.TextRuns.Select(run => CreateSpan(run, text, source, paints)).ToArray();
            bool addedReference = false;
            _handle.DangerousAddRef(ref addedReference);
            try
            {
                return CreateNativeLayout(source, text, options, fontId, fallbacks, features, variations, spans, paints);
            }
            finally
            {
                if (addedReference)
                {
                    _handle.DangerousRelease();
                }
            }
        }
    }

    /// <summary>Releases registered native fonts and caches while preserving completed layout snapshots.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                _disposed = true;
                _handle.Dispose();
            }
        }
    }

    private static void ValidateOptions(ParleyTextOptions2D options)
    {
        Positive(options.FontSize, nameof(options.FontSize));
        Positive(options.LineHeight, nameof(options.LineHeight));
        if (float.IsNaN(options.WrappingLength) || options.WrappingLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options.WrappingLength));
        }

        if (!Enum.IsDefined(options.Alignment) || !Enum.IsDefined(options.BaseDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(options.ColorPaletteIndex);
        Decorations(options.Decorations);
        ArgumentNullException.ThrowIfNull(options.FallbackFontFamilies);
        ArgumentNullException.ThrowIfNull(options.FontFeatures);
        ArgumentNullException.ThrowIfNull(options.FontVariations);
        ArgumentNullException.ThrowIfNull(options.TextRuns);
        if (options.Locale is string locale && (locale.Length is 0 or > 255 || locale.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
        {
            throw new ArgumentException("Locale must be a BCP 47 language tag.", nameof(options.Locale));
        }

        foreach (ParleyFontFeature2D feature in options.FontFeatures)
        {
            if (feature.Value > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(options.FontFeatures), "Feature values must fit an unsigned sixteen-bit number.");
            }
        }

        foreach (ParleyFontVariation2D variation in options.FontVariations)
        {
            if (!float.IsFinite(variation.Value))
            {
                throw new ArgumentOutOfRangeException(nameof(options.FontVariations));
            }
        }
    }

    private static void Positive(float value, string name)
    {
        if (!float.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, "The value must be positive and finite.");
        }
    }

    private static void Decorations(ParleyTextDecorations2D value)
    {
        if ((value & ~(ParleyTextDecorations2D.Underline | ParleyTextDecorations2D.Strikethrough | ParleyTextDecorations2D.Overline)) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
    }

    private uint FindFont(string family)
    {
        ArgumentNullException.ThrowIfNull(family);
        return _families.TryGetValue(family, out uint id) ? id : throw new ArgumentException($"The font family '{family}' has not been registered.", nameof(family));
    }

    private LpSpan CreateSpan(ParleyTextRun2D run, string text, Utf8Text2D source, List<PaintGraph2D> paints)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Start < 0 || run.End < run.Start || run.End > text.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(run), "Text-run ranges must be ordered UTF-16 indices inside the text.");
        }

        if (run.FontSize is float size)
        {
            Positive(size, nameof(run.FontSize));
        }

        if (run.FontWeight is float weight && (!float.IsFinite(weight) || weight < 1 || weight > 1000))
        {
            throw new ArgumentOutOfRangeException(nameof(run.FontWeight));
        }

        if (run.Decorations is ParleyTextDecorations2D decorations)
        {
            Decorations(decorations);
        }

        uint brush = uint.MaxValue;
        if (run.Paint is not null)
        {
            brush = checked((uint)paints.Count);
            paints.Add(run.Paint);
        }

        return new LpSpan
        {
            start = checked((uint)source.Utf8Offset(run.Start)),
            end = checked((uint)source.Utf8Offset(run.End)),
            font_id = run.FontFamily is null ? uint.MaxValue : FindFont(run.FontFamily),
            brush_index = brush,
            font_size = run.FontSize ?? 0,
            weight = run.FontWeight ?? 0,
            italic = run.Italic.HasValue ? run.Italic.Value ? 1u : 0u : 2,
            decorations = run.Decorations.HasValue ? (uint)run.Decorations.Value : uint.MaxValue,
        };
    }

    private ParleyTextLayout2D CreateNativeLayout(Utf8Text2D source, string text, ParleyTextOptions2D options, uint fontId, uint[] fallbacks, LpFeature[] features, LpVariation[] variations, LpSpan[] spans, IReadOnlyList<PaintGraph2D> paints)
    {
        byte[] locale = Encoding.UTF8.GetBytes(options.Locale ?? string.Empty);
        fixed (byte* textBytes = source.Bytes, localeBytes = locale)
        {
            fixed (uint* fallbackIds = fallbacks)
            {
                fixed (LpFeature* featureValues = features)
                {
                    fixed (LpVariation* variationValues = variations)
                    {
                        fixed (LpSpan* spanValues = spans)
                        {
                            var settings = new LpOptions
                            {
                                font_size = options.FontSize,
                                max_width = float.IsPositiveInfinity(options.WrappingLength) ? 0 : options.WrappingLength,
                                line_height = options.LineHeight,
                                font_id = fontId,
                                alignment = (uint)options.Alignment,
                                direction = (uint)options.BaseDirection,
                                palette_index = (uint)options.ColorPaletteIndex,
                                decorations = (uint)options.Decorations,
                                fallback_font_ids = fallbackIds,
                                fallback_font_count = (nuint)fallbacks.Length,
                                features = featureValues,
                                feature_count = (nuint)features.Length,
                                variations = variationValues,
                                variation_count = (nuint)variations.Length,
                                locale = localeBytes,
                                locale_len = (nuint)locale.Length,
                            };
                            LpLayout* layout = null;
                            ParleyNativeStatus.Check(NativeMethods.lp_layout_create((LpEngine*)_handle.DangerousGetHandle(), textBytes, (nuint)source.Bytes.Length, &settings, spanValues, (nuint)spans.Length, &layout));
                            if (layout is null)
                            {
                                throw new InvalidOperationException("The native engine returned an empty layout owner.");
                            }

                            using var owner = new ParleyLayoutHandle((nint)layout);
                            LpLayoutView view = default;
                            ParleyNativeStatus.Check(NativeMethods.lp_layout_view(layout, &view));
                            ParleyTextLayout2D snapshot = ParleyLayoutImporter2D.Import(text, source, view, paints, _fontNames);
                            GC.KeepAlive(owner);
                            return snapshot;
                        }
                    }
                }
            }
        }
    }
}
