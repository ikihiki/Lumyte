using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Browser;
using Lumyte.Graphics.Drawing2D;
using SixLabors.Fonts;

namespace Lumyte.Graphics.Text.Browser.Sample;

[SupportedOSPlatform("browser")]
internal static partial class Program
{
    private const uint Width = 900;
    private const uint Height = 1280;
    private static string _report = string.Empty;
    private static string _pixels = string.Empty;
    private static Rect2D _counterBounds;
    private static Rect2D[] _distanceCounters = [];
    private static Rect2D[] _colorRegions = [];

    [JSExport]
    internal static string GetReport() => _report;

    [JSExport]
    internal static string GetPixelsBase64() => _pixels;

    private static async Task Main()
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lumyte.Text.Sample.DejaVuSans.ttf") ?? throw new InvalidOperationException("The bundled font is missing.");
        var collection = new FontCollection();
        FontFamily family = collection.Add(stream);
        using Stream japaneseStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lumyte.Text.Sample.NotoSansJP.ttf") ?? throw new InvalidOperationException("The bundled Japanese font is missing.");
        FontFamily japanese = collection.Add(japaneseStream);
        using Stream colorStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lumyte.Text.Sample.ColorFontTest.ttf") ?? throw new InvalidOperationException("The bundled COLRv1 font is missing.");
        using var colorFont = ColorFont2D.Load(colorStream);
        using BrowserDevice device = await BrowserDevice.CreateAsync("./lumyte-graphics.js");
        var ink = PaintGraph2D.Solid(new Color2D(0.1f, 0.15f, 0.25f, 1));
        TextLayout2D distanceLayout = SixLaborsTextLayout2D.Create("OAV", new TextOptions(family.CreateFont(20)), ink);
        TextLayout2D counterLayout = SixLaborsTextLayout2D.Create("O", new TextOptions(family.CreateFont(20)), ink);
        using DistanceFieldText2D msdf = await DistanceFieldText2D.CreateAsync(device, distanceLayout, mode: DistanceFieldMode2D.Msdf);
        using DistanceFieldText2D mtsdf = await DistanceFieldText2D.CreateAsync(device, distanceLayout, mode: DistanceFieldMode2D.Mtsdf);
        const string ColorGlyphs = "\U000F0500\U000F0503\U000F0200\U000F0A17";
        var colorOptions = new TextOptions(colorFont.Family.CreateFont(70));
        TextLayout2D colorLayout = SixLaborsTextLayout2D.Create(ColorGlyphs, colorOptions, colorFonts: [colorFont]);
        _colorRegions = colorLayout.Drawings.OfType<TextPaintGeometry2D>()
            .Select(glyph => glyph.Bounds with { X = glyph.Bounds.X + 520, Y = glyph.Bounds.Y + 545 }).ToArray();
        if (_colorRegions.Length != 4)
        {
            throw new InvalidOperationException("The COLRv1 glyphs were not routed through vector paint graphs.");
        }

        Rect2D bounds = counterLayout.Bounds;
        _distanceCounters = [
            new Rect2D(36 + (bounds.X * 4), 475 + (bounds.Y * 4), bounds.Width * 4, bounds.Height * 4),
            new Rect2D(280 + (bounds.X * 4), 475 + (bounds.Y * 4), bounds.Width * 4, bounds.Height * 4),
        ];
        DrawList2D list = BuildDrawing(family, japanese, msdf, mtsdf, colorLayout, colorFont);
        using IGraphicsTexture texture = device.CreateTexture(new()
        {
            Width = Width,
            Height = Height,
            Format = TextureFormat.Rgba8Srgb,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
        });
        using IGraphicsTextureView view = texture.CreateView();
        uint alignment = device.GetTextureCopyLayout(texture.Format).BytesPerRowAlignment;
        uint pitch = (((Width * 4) + alignment - 1) / alignment) * alignment;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new()
        {
            Count = pitch * Height,
            Usage = BufferUsage.CopyDestination,
            Memory = MemoryPreference.Readback,
        });
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using PreparedDraw2D prepared = await renderer.PrepareAsync(list, new RenderTarget2D
        {
            View = view,
            LoadOp = AttachmentLoadOp.Clear,
            ClearColor = new Color2D(0.96f, 0.97f, 0.99f, 1),
        });
        using (IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new()))
        {
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
            var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
            commands.Barrier(new TextureBarrierDesc
            {
                Texture = texture,
                Range = new(0, 1, 0, 1),
                BeforeState = TextureState.Undefined,
                AfterState = TextureState.ColorAttachment,
                Before = default,
                After = color,
            });
            msdf.RecordUpload(commands);
            mtsdf.RecordUpload(commands);
            prepared.Record(commands);
            commands.Barrier(new TextureBarrierDesc
            {
                Texture = texture,
                Range = new(0, 1, 0, 1),
                BeforeState = TextureState.ColorAttachment,
                AfterState = TextureState.CopySource,
                Before = color,
                After = copy,
            });
            commands.CopyTextureToBuffer(new() { Texture = texture, Width = Width, Height = Height }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = Height });
            commands.Barrier(new BufferBarrierDesc<byte>
            {
                Buffer = readback.Slice(0, readback.Count),
                Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite),
                After = new(PipelineStage.Host, ResourceAccess.HostRead),
            });
            commands.Finish();
            using IGraphicsSubmission submission = device.Queue.Submit([commands]);
            await submission.WaitAsync();
        }

        await readback.MapAsync();
        byte[] padded = new byte[checked((int)readback.Count)];
        readback.CopyTo(padded);
        readback.Unmap();
        byte[] packed = new byte[checked((int)(Width * Height * 4))];
        for (uint y = 0; y < Height; y++)
        {
            padded.AsSpan(checked((int)(y * pitch)), checked((int)(Width * 4))).CopyTo(packed.AsSpan(checked((int)(y * Width * 4))));
        }

        int darkPixels = 0;
        for (int i = 0; i < packed.Length; i += 4)
        {
            if (packed[i] < 160 && packed[i + 1] < 160 && packed[i + 2] < 160)
            {
                darkPixels++;
            }
        }

        if (darkPixels < 2000)
        {
            throw new InvalidOperationException("GPU readback contained too little text ink.");
        }

        CheckGlyphCounter(packed, _counterBounds);
        foreach (Rect2D distanceCounter in _distanceCounters)
        {
            CheckGlyphCounter(packed, distanceCounter);
        }

        CheckColorGlyphs(packed);
        CheckBackendPaintPixels(packed);
        _pixels = Convert.ToBase64String(packed);
        _report = $"WASM text rendering passed: {darkPixels} ink pixels, O counter preserved. MSDF/MTSDF x4 counters, COLRv1 vector gradients and multiply. Backend checks: all 28 composite modes, isolated backdrops, clip masks and gradient edge cases. Bundled fonts, Japanese fallback, bidi, wrapping, decorations and clipping.";
        Console.WriteLine(_report);
    }

    private static void CheckGlyphCounter(byte[] pixels, Rect2D bounds)
    {
        int centerX = (int)(bounds.X + (bounds.Width / 2));
        int centerY = (int)(bounds.Y + (bounds.Height / 2));
        int center = ((centerY * (int)Width) + centerX) * 4;
        if (pixels[center] < 240 || pixels[center + 1] < 240 || pixels[center + 2] < 240)
        {
            throw new InvalidOperationException("The O glyph counter was filled.");
        }

        int edgeInk = 0;
        for (int y = (int)bounds.Y; y < bounds.Y + bounds.Height; y++)
        {
            for (int x = (int)bounds.X; x < bounds.X + bounds.Width; x++)
            {
                int pixel = ((y * (int)Width) + x) * 4;
                if (pixels[pixel] < 160 && pixels[pixel + 1] < 160 && pixels[pixel + 2] < 160)
                {
                    edgeInk++;
                }
            }
        }

        if (edgeInk < 200)
        {
            throw new InvalidOperationException("The O glyph outline was missing.");
        }
    }

    private static void CheckColorGlyphs(byte[] pixels)
    {
        foreach (Rect2D bounds in _colorRegions)
        {
            var colors = new HashSet<int>();
            int saturated = 0;
            for (int y = Math.Max(0, (int)bounds.Y); y < Math.Min(Height, bounds.Y + bounds.Height); y++)
            {
                for (int x = Math.Max(0, (int)bounds.X); x < Math.Min(Width, bounds.X + bounds.Width); x++)
                {
                    int pixel = ((y * (int)Width) + x) * 4;
                    int red = pixels[pixel];
                    int green = pixels[pixel + 1];
                    int blue = pixels[pixel + 2];
                    if (Math.Max(red, Math.Max(green, blue)) - Math.Min(red, Math.Min(green, blue)) > 50)
                    {
                        saturated++;
                        colors.Add(((red / 16) << 8) | ((green / 16) << 4) | (blue / 16));
                    }
                }
            }

            if (saturated < 100 || colors.Count < 4)
            {
                throw new InvalidOperationException("A COLRv1 gradient or composite glyph lost its colors in GPU readback.");
            }
        }
    }

    private static DrawList2D BuildDrawing(FontFamily family, FontFamily japanese, DistanceFieldText2D msdf, DistanceFieldText2D mtsdf, TextLayout2D colorLayout, ColorFont2D colorFont)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        var ink = PaintGraph2D.Solid(new Color2D(0.1f, 0.15f, 0.25f, 1));
        TextLayout2D title = SixLaborsTextLayout2D.Create("Lumyte / vector text", new TextOptions(family.CreateFont(36)), ink);
        canvas.DrawText(title, new Vector2(36, 22));
        var options = new TextOptions(family.CreateFont(25))
        {
            WrappingLength = 800,
            LineSpacing = 1.3f,
            TextRuns =
            [
                new TextRun2D
                {
                    Start = 0,
                    End = 10,
                    TextDecorations = TextDecorations.Underline,
                    Paint = PaintGraph2D.Solid(new Color2D(0.08f, 0.4f, 0.8f, 1)),
                },
            ],
        };
        TextLayout2D paragraph = SixLaborsTextLayout2D.Create("Rich text: fonts, kerning, combining marks (cafe\u0301), wrapping and decoration share the 2D renderer.", options, ink);
        canvas.DrawText(paragraph, new Vector2(36, 100));
        TextLayout2D mixed = SixLaborsTextLayout2D.Create("English + العربية + עברית", new TextOptions(family.CreateFont(28)), ink);
        canvas.DrawText(mixed, new Vector2(36, 228));
        TextLayout2D counter = SixLaborsTextLayout2D.Create("O", new TextOptions(family.CreateFont(100)), ink);
        canvas.DrawText(counter, new Vector2(730, 210));
        _counterBounds = counter.Bounds with { X = counter.Bounds.X + 730, Y = counter.Bounds.Y + 210 };
        var japaneseOptions = new TextOptions(family.CreateFont(24)) { FallbackFontFamilies = [japanese] };
        TextLayout2D japaneseLine = SixLaborsTextLayout2D.Create("日本語の文字描画：折り返しと装飾", japaneseOptions, ink);
        canvas.DrawText(japaneseLine, new Vector2(36, 278));
        var gradient = PaintGraph2D.LinearGradient(
            new Vector2(0, 0),
            new Vector2(650, 0),
            [new(0, new Color2D(0.55f, 0.15f, 0.8f, 1)), new(1, new Color2D(0.02f, 0.5f, 0.55f, 1))]);
        TextLayout2D transformed = SixLaborsTextLayout2D.Create("Scalable outlines / clipped", new TextOptions(family.CreateFont(34)), gradient);
        canvas.Save();
        canvas.ClipRect(new Rect2D(36, 316, 800, 82));
        canvas.Translate(new Vector2(36, 330));
        canvas.Rotate(-0.04f);
        canvas.Scale(new Vector2(1.1f));
        canvas.DrawText(transformed);
        canvas.Restore();
        canvas.DrawText(SixLaborsTextLayout2D.Create("MSDF x4", new TextOptions(family.CreateFont(22)), ink), new Vector2(36, 416));
        canvas.DrawText(SixLaborsTextLayout2D.Create("MTSDF x4", new TextOptions(family.CreateFont(22)), ink), new Vector2(280, 416));
        canvas.DrawText(SixLaborsTextLayout2D.Create("COLRv1 / gradients + blend", new TextOptions(family.CreateFont(20)), ink), new Vector2(520, 416));
        canvas.Save();
        canvas.Translate(new Vector2(36, 475));
        canvas.Scale(new Vector2(4));
        msdf.Draw(canvas);
        canvas.Restore();
        canvas.Save();
        canvas.Translate(new Vector2(280, 475));
        canvas.Scale(new Vector2(4));
        mtsdf.Draw(canvas);
        canvas.Restore();
        canvas.DrawText(colorLayout, new Vector2(520, 545));
        BuildBackendPaintChecks(canvas, family);
        BuildCurrentColorChecks(canvas, colorFont);
        return canvas.End();
    }
}
