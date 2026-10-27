using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Browser;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text.Parley.Browser.Sample;

[SupportedOSPlatform("browser")]
internal static partial class Program
{
    private const uint Width = 900;
    private const uint Height = 1700;
    private static string _report = string.Empty;
    private static string _pixels = string.Empty;

    [JSExport]
    internal static string GetReport() => _report;

    [JSExport]
    internal static string GetPixelsBase64() => _pixels;

    private static async Task Main()
    {
        try
        {
            await RunAsync();
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            throw;
        }
    }

    private static async Task RunAsync()
    {
        using var engine = new ParleyTextEngine2D();
        Console.WriteLine("Parley engine created through the generated C ABI.");
        string family = AddFont(engine, "DejaVuSans");
        string japanese = AddFont(engine, "NotoSansJP");
        string khmer = AddFont(engine, "NotoSansKhmer");
        string color = AddFont(engine, "ColorFontTest");
        var options = new ParleyTextOptions2D { FontFamily = family, FontSize = 26 };
        CheckShaping(engine, options, japanese);
        Console.WriteLine("Parley font registration and paragraph shaping checks passed.");
        var ink = PaintGraph2D.Solid(new Color2D(0.1f, 0.15f, 0.25f, 1));
        TextLayout2D counter = engine.CreateLayout("O", options with { FontSize = 100 }, ink);
        TextLayout2D smallCounter = engine.CreateLayout("O", options with { FontSize = 20 }, ink);
        TextLayout2D distance = engine.CreateLayout("OAV", options with { FontSize = 20 }, ink);
        TextLayout2D colorLayout = engine.CreateLayout("\U000F0500\U000F0503\U000F0200\U000F0A17", options with { FontFamily = color, FontSize = 70 });
        var foreground = new Color2D(0.9f, 0.1f, 0.2f, 0.5f);
        TextLayout2D translucent = engine.CreateLayout("\U000F0B06", options with { FontFamily = color, FontSize = 80 }, PaintGraph2D.Solid(foreground));
        var foregroundEnd = new Color2D(0.1f, 0.5f, 0.9f, 0.5f);
        TextLayout2D gradient = engine.CreateLayout("\U000F0B06", options with { FontFamily = color, FontSize = 80 }, PaintGraph2D.LinearGradient(
            Vector2.Zero,
            new Vector2(80, 0),
            [new(0, foreground), new(1, foregroundEnd)]));
        if (colorLayout.Drawings.OfType<TextPaintGeometry2D>().Count() != 4 || translucent.Drawings.OfType<TextPaintGeometry2D>().Count() != 1 || gradient.Drawings.OfType<TextPaintGeometry2D>().Count() != 1)
        {
            throw new InvalidOperationException("COLRv1 glyphs must produce vector paint graphs.");
        }

        using BrowserDevice device = await BrowserDevice.CreateAsync("./lumyte-graphics.js");
        Console.WriteLine("Browser WebGPU device created.");
        using DistanceFieldText2D msdf = await DistanceFieldText2D.CreateAsync(device, distance, mode: DistanceFieldMode2D.Msdf);
        using DistanceFieldText2D mtsdf = await DistanceFieldText2D.CreateAsync(device, distance, mode: DistanceFieldMode2D.Mtsdf);
        DrawList2D drawing = BuildDrawing(engine, options, japanese, khmer, counter, colorLayout, translucent, gradient, msdf, mtsdf);

        // A layout owns managed geometry and paint snapshots, independent of Rust font memory.
        engine.Dispose();
        byte[] pixels = await RenderAsync(device, drawing, msdf, mtsdf);
        int inkPixels = 0;
        int textInkPixels = 0;
        for (int index = 0; index < pixels.Length; index += 4)
        {
            if (pixels[index] < 160 && pixels[index + 1] < 160 && pixels[index + 2] < 160)
            {
                inkPixels++;
                if (index < Width * 1000 * 4)
                {
                    textInkPixels++;
                }
            }
        }

        if (textInkPixels < 2000)
        {
            throw new InvalidOperationException("GPU readback contained too little text ink.");
        }

        CheckCounter(pixels, Translate(counter.Bounds, new Vector2(760, 100)));
        CheckCounter(pixels, ScaleAndTranslate(smallCounter.Bounds, 4, new Vector2(24, 430)));
        CheckCounter(pixels, ScaleAndTranslate(smallCounter.Bounds, 4, new Vector2(320, 430)));
        foreach (TextPaintGeometry2D glyph in colorLayout.Drawings.OfType<TextPaintGeometry2D>())
        {
            CheckColors(pixels, Translate(glyph.Bounds, new Vector2(24, 610)));
        }

        CheckForeground(pixels, translucent.Drawings.OfType<TextPaintGeometry2D>().Single().Bounds, new Vector2(24, 780), foreground, foreground, 80);
        CheckForeground(pixels, gradient.Drawings.OfType<TextPaintGeometry2D>().Single().Bounds, new Vector2(230, 780), foreground, foregroundEnd, 80);
        CheckBackendPaintPixels(pixels);
        _pixels = Convert.ToBase64String(pixels);
        _report = $"Parley WASM text rendering passed: {inkPixels} ink pixels ({textInkPixels} in the text panel). csbindgen C ABI, ligatures, Japanese fallback, bidi clusters and wrapping. MSDF/MTSDF x4 counters, COLRv1 vector gradients and multiply, translucent and gradient currentColor. Backend checks: all 28 composite modes, isolated backdrops, clip masks and gradient edge cases. Layouts survive engine disposal.";
        Console.WriteLine(_report);
    }

    private static string AddFont(ParleyTextEngine2D engine, string name)
    {
        using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Lumyte.Parley.Sample.{name}.ttf") ?? throw new InvalidOperationException($"The bundled font {name} is missing.");
        return engine.AddFont(stream);
    }

    private static void CheckShaping(ParleyTextEngine2D engine, ParleyTextOptions2D options, string japanese)
    {
        ParleyTextLayout2D ligatures = engine.CreateDetailedLayout("office", options);
        ParleyTextLayout2D unjoined = engine.CreateDetailedLayout("office", options with { FontFeatures = [new("liga", 0)] });
        if (ligatures.Glyphs.Count >= unjoined.Glyphs.Count)
        {
            throw new InvalidOperationException("OpenType ligatures did not affect shaping.");
        }

        ParleyTextLayout2D fallback = engine.CreateDetailedLayout("日本語", options with { FallbackFontFamilies = [japanese] });
        if (fallback.Glyphs.Count != 3 || fallback.Glyphs.Any(glyph => glyph.GlyphId == 0 || glyph.FontFamily != japanese))
        {
            throw new InvalidOperationException("Explicit Japanese fallback was not used.");
        }

        ParleyTextLayout2D bidi = engine.CreateDetailedLayout("ABC אבג", options);
        int[] rtlClusters = bidi.Glyphs.Where(glyph => glyph.Utf16Start >= 4).Select(glyph => glyph.Utf16Start).ToArray();
        if (!rtlClusters.SequenceEqual([6, 5, 4]))
        {
            throw new InvalidOperationException("Bidi source clusters did not follow visual glyph order.");
        }

        TextLayout2D wrapped = engine.CreateLayout("Parley shapes a paragraph and wraps its positioned glyphs.", options with { WrappingLength = 230 });
        if (wrapped.LineCount < 2)
        {
            throw new InvalidOperationException("Paragraph wrapping did not create multiple lines.");
        }
    }

    private static DrawList2D BuildDrawing(ParleyTextEngine2D engine, ParleyTextOptions2D options, string japanese, string khmer, TextLayout2D counter, TextLayout2D color, TextLayout2D translucent, TextLayout2D gradient, DistanceFieldText2D msdf, DistanceFieldText2D mtsdf)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        var ink = PaintGraph2D.Solid(new Color2D(0.1f, 0.15f, 0.25f, 1));
        canvas.DrawText(engine.CreateLayout("Parley / csbindgen / WebGPU", options with { FontSize = 34 }, ink), new Vector2(24, 20));
        ParleyTextOptions2D paragraphOptions = options with
        {
            WrappingLength = 680,
            LineHeight = 1.3f,
            TextRuns =
            [
                new ParleyTextRun2D
                {
                    Start = 2,
                    End = 20,
                    Decorations = ParleyTextDecorations2D.Underline,
                    Paint = PaintGraph2D.Solid(new Color2D(0.08f, 0.4f, 0.8f, 1)),
                },
            ],
        };
        canvas.DrawText(engine.CreateLayout("A reusable paragraph: office ligatures, combining marks (cafe\u0301), wrapping, fallback and vector paints.", paragraphOptions, ink), new Vector2(24, 90));
        canvas.DrawText(engine.CreateLayout("English + العربية + עברית", options with { FontSize = 28 }, ink), new Vector2(24, 205));
        canvas.DrawText(engine.CreateLayout("日本語の文字描画：折り返しと装飾", options with { FallbackFontFamilies = [japanese] }, ink), new Vector2(24, 260));
        canvas.DrawText(engine.CreateLayout("ខ្មែរ", options with { FontFamily = khmer, FontSize = 32 }, ink), new Vector2(24, 315));
        canvas.DrawText(counter, new Vector2(760, 100));
        canvas.DrawText(engine.CreateLayout("MSDF x4", options with { FontSize = 22 }, ink), new Vector2(24, 380));
        canvas.DrawText(engine.CreateLayout("MTSDF x4", options with { FontSize = 22 }, ink), new Vector2(320, 380));
        canvas.Save();
        canvas.Translate(new Vector2(24, 430));
        canvas.Scale(new Vector2(4));
        msdf.Draw(canvas);
        canvas.Restore();
        canvas.Save();
        canvas.Translate(new Vector2(320, 430));
        canvas.Scale(new Vector2(4));
        mtsdf.Draw(canvas);
        canvas.Restore();
        canvas.DrawText(engine.CreateLayout("COLRv1: gradients and multiply", options with { FontSize = 22 }, ink), new Vector2(24, 560));
        canvas.DrawText(color, new Vector2(24, 610));
        canvas.DrawText(engine.CreateLayout("currentColor: alpha 0.5 / gradient", options with { FontSize = 22 }, ink), new Vector2(24, 730));
        canvas.DrawText(translucent, new Vector2(24, 780));
        canvas.DrawText(gradient, new Vector2(230, 780));
        BuildBackendPaintChecks(canvas, engine, options);
        return canvas.End();
    }

    private static async Task<byte[]> RenderAsync(BrowserDevice device, DrawList2D drawing, DistanceFieldText2D msdf, DistanceFieldText2D mtsdf)
    {
        using IGraphicsTexture texture = device.CreateTexture(new() { Width = Width, Height = Height, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        using IGraphicsTextureView view = texture.CreateView();
        uint alignment = device.GetTextureCopyLayout(texture.Format).BytesPerRowAlignment;
        uint pitch = (((Width * 4) + alignment - 1) / alignment) * alignment;
        using IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new() { Count = pitch * Height, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using PreparedDraw2D prepared = await renderer.PrepareAsync(drawing, new RenderTarget2D { View = view, LoadOp = AttachmentLoadOp.Clear, ClearColor = new Color2D(1, 1, 1, 1) });
        using (IGraphicsCommandBuffer commands = device.CreateCommandBuffer(new()))
        {
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
            var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = color });
            msdf.RecordUpload(commands);
            mtsdf.RecordUpload(commands);
            prepared.Record(commands);
            commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = color, After = copy });
            commands.CopyTextureToBuffer(new() { Texture = texture, Width = Width, Height = Height }, new() { Buffer = readback.Slice(0, readback.Count), BytesPerRow = pitch, RowsPerImage = Height });
            commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.Slice(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
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

        return packed;
    }

    private static Rect2D Translate(Rect2D bounds, Vector2 origin) => bounds with { X = bounds.X + origin.X, Y = bounds.Y + origin.Y };

    private static Rect2D ScaleAndTranslate(Rect2D bounds, float scale, Vector2 origin) => new((bounds.X * scale) + origin.X, (bounds.Y * scale) + origin.Y, bounds.Width * scale, bounds.Height * scale);

    private static void CheckCounter(byte[] pixels, Rect2D bounds)
    {
        int centerX = (int)(bounds.X + (bounds.Width / 2));
        int centerY = (int)(bounds.Y + (bounds.Height / 2));
        int center = ((centerY * (int)Width) + centerX) * 4;
        if (pixels[center] < 240 || pixels[center + 1] < 240 || pixels[center + 2] < 240)
        {
            throw new InvalidOperationException("The O glyph counter was filled.");
        }

        int ink = 0;
        for (int y = Math.Max(0, (int)bounds.Y); y < Math.Min(Height, bounds.Y + bounds.Height); y++)
        {
            for (int x = Math.Max(0, (int)bounds.X); x < Math.Min(Width, bounds.X + bounds.Width); x++)
            {
                int pixel = ((y * (int)Width) + x) * 4;
                ink += pixels[pixel] < 160 && pixels[pixel + 1] < 160 && pixels[pixel + 2] < 160 ? 1 : 0;
            }
        }

        if (ink < 200)
        {
            throw new InvalidOperationException("The O glyph outline was missing.");
        }
    }

    private static void CheckColors(byte[] pixels, Rect2D bounds)
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
            throw new InvalidOperationException("A COLRv1 gradient or composite glyph lost its colors.");
        }
    }

    private static void CheckForeground(byte[] pixels, Rect2D bounds, Vector2 origin, Color2D start, Color2D end, float width)
    {
        int x = (int)(origin.X + bounds.X + (bounds.Width / 2));
        int y = (int)(origin.Y + bounds.Y + (bounds.Height / 2));
        float amount = Math.Clamp(((x + 0.5f) - origin.X) / width, 0, 1);
        float[] expected =
        [
            Encode((float.Lerp(Decode(start.R), Decode(end.R), amount) * start.A) + (1 - start.A)),
            Encode((float.Lerp(Decode(start.G), Decode(end.G), amount) * start.A) + (1 - start.A)),
            Encode((float.Lerp(Decode(start.B), Decode(end.B), amount) * start.A) + (1 - start.A)),
        ];
        int pixel = ((y * (int)Width) + x) * 4;
        for (int channel = 0; channel < 3; channel++)
        {
            int target = (int)MathF.Round(expected[channel] * 255);
            if (Math.Abs(pixels[pixel + channel] - target) > 4)
            {
                throw new InvalidOperationException($"COLRv1 currentColor mismatch at ({x},{y}), channel {channel}: {pixels[pixel + channel]} != {target}.");
            }
        }
    }

    private static float Decode(float value) => value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);

    private static float Encode(float value) => value <= 0.0031308f ? value * 12.92f : (1.055f * MathF.Pow(value, 1 / 2.4f)) - 0.055f;
}
