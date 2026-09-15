using System.Numerics;

using Lumyte.Graphics.Passes;
using Lumyte.Graphics.TwoD;

namespace Lumyte.Graphics.RenderGraph.Conformance;

/// <summary>The same compiled consumer and independent scalar reference are used by every backend.</summary>
public static class ImageFilterConsumer
{
    public static readonly string[] Cases = ["nearest", "linear", "blur-zero", "blur-edges", "blur-large", "tonemap", "tonemap-high", "tonemap-low", "composite-clear", "composite-preserve", "empty-clear", "empty-preserve"];
    private static readonly Vector4[] Pixels = [new(2, .25f, .5f, .5f), new(0, 0, 0, 0), new(.25f, .5f, .75f, 1), new(.5f, .125f, 0, .25f), new(.25f, .5f, .125f, .5f), new(1, 0, .25f, 1)];
    public static ImageFilterFixture Create(string name)
    {
        var graph = new GpuRenderGraph();
        var description = new GpuGraphTextureDescription(3, 2, GpuFormat.Rgba16Float);
        var source = graph.CreateTexture("source", description);
        graph.AddClearPass("initialize source", new(source, TextureClearValue.Color(Vector4.Zero)));
        using var scene = new Draw2DSceneBuilder();
        for (int i = 0; i < Pixels.Length; i++)
        {
            Vector4 p = Pixels[i];
            scene.FillRectangle(new(i % 3, i / 3, 1, 1), new SolidBrush(new Color(p.W > 0 ? p.X / p.W : 0, p.W > 0 ? p.Y / p.W : 0, p.W > 0 ? p.Z / p.W : 0, p.W)));
        }
        graph.Add2DPass("source pixels", new(scene.Finish(), source));
        GpuRenderGraphTexture target;
        Vector4[] expected;
        if (name is "nearest" or "linear")
        {
            target = graph.CreateTexture("target", new(7, 5, GpuFormat.Rgba16Float));
            var filter = graph.CreateInput("filter", ImageSamplingInputContract.Instance, name == "nearest" ? ImageSampling.Nearest : ImageSampling.Linear);
            graph.AddBlitPass("blit", new(source, target, filter));
            expected = Enumerable.Range(0, 35).Select(i => Sample((i % 7 + .5f) * 3 / 7 - .5f, (i / 7 + .5f) * 2 / 5 - .5f, name == "linear")).ToArray();
        }
        else if (name.StartsWith("blur", StringComparison.Ordinal))
        {
            int radius = name == "blur-zero" ? 0 : name == "blur-edges" ? 2 : int.MaxValue;
            var input = graph.CreateInput("radius", BlurRadiusInputContract.Instance, radius);
            target = graph.AddBlurPass("blur", new(source, input)).Color;
            expected = Enumerable.Range(0, 6).Select(i => Box(i % 3, i / 3, radius)).ToArray();
        }
        else if (name.StartsWith("tonemap", StringComparison.Ordinal))
        {
            float exposure = name == "tonemap" ? 1 : name == "tonemap-high" ? 1000 : -1000;
            var input = graph.CreateInput("exposure", ExposureInputContract.Instance, exposure);
            target = graph.AddToneMapPass("tonemap", new(source, input)).Color;
            expected = Pixels.Select(p => p.W == 0 ? Vector4.Zero : new Vector4(Map(p.X / p.W, exposure) * p.W, Map(p.Y / p.W, exposure) * p.W, Map(p.Z / p.W, exposure) * p.W, p.W)).ToArray();
        }
        else
        {
            bool preserve = name.EndsWith("preserve", StringComparison.Ordinal);
            bool empty = name.StartsWith("empty", StringComparison.Ordinal);
            target = graph.CreateTexture("target", description);
            Vector4 background = new(.125f, .25f, .5f, .5f);
            graph.AddClearPass("old target", new(target, TextureClearValue.Color(background)));
            var opacity = graph.CreateInput("opacity", CompositeOpacityInputContract.Instance, .5f);
            graph.AddCompositePass("composite", new(target, empty ? [] : [new(source, opacity), new(source, .25f)],
                preserve ? TargetContent.Preserve : TargetContent.Clear, new Vector4(.5f, .25f, .125f, .25f)));
            Vector4 initial = preserve ? background : new(.5f, .25f, .125f, .25f);
            initial = new(initial.X * initial.W, initial.Y * initial.W, initial.Z * initial.W, initial.W);
            expected = Pixels.Select(p => empty ? initial : Over(p * .25f, Over(p * .5f, initial))).ToArray();
        }
        graph.ExportTexture(target);
        return new(graph.Compile(), target, target.Description, expected);
    }
    private static Vector4 Over(Vector4 source, Vector4 destination) => source + destination * (1 - source.W);
    private static float Map(float x, float exposure) => x <= 0 ? 0 : (float)(1 / (1 + Math.Pow(2, -exposure) / x));
    private static Vector4 Pixel(int x, int y) => Pixels[Math.Clamp(y, 0, 1) * 3 + Math.Clamp(x, 0, 2)];
    private static Vector4 Sample(float x, float y, bool linear)
    {
        if (!linear)
        { return Pixel((int)MathF.Floor(x + .5f), (int)MathF.Floor(y + .5f)); }
        int ix = (int)MathF.Floor(x), iy = (int)MathF.Floor(y);
        return Vector4.Lerp(Vector4.Lerp(Pixel(ix, iy), Pixel(ix + 1, iy), x - ix), Vector4.Lerp(Pixel(ix, iy + 1), Pixel(ix + 1, iy + 1), x - ix), y - iy);
    }
    private static Vector4 Box(int x, int y, int radius)
    {
        // Sum the six source texels with exact integer multiplicities, independent of shader loops.
        double divisor = (2.0 * radius + 1) * (2.0 * radius + 1);
        Vector4 result = default;
        for (int sy = 0; sy < 2; sy++)
        {
            for (int sx = 0; sx < 3; sx++)
            {
                long xCount = Multiplicity(x, sx, 3, radius), yCount = Multiplicity(y, sy, 2, radius);
                result += Pixel(sx, sy) * (float)((double)xCount * yCount / divisor);
            }
        }
        return result;
    }
    private static long Multiplicity(int center, int sample, int extent, int radius)
    {
        long lo = (long)center - radius, hi = (long)center + radius;
        long first = sample == 0 ? lo : sample, last = sample == extent - 1 ? hi : sample;
        return Math.Max(0, Math.Min(hi, last) - Math.Max(lo, first) + 1);
    }
    public static void Compare(ImageFilterFixture fixture, ReadOnlySpan<byte> pixels, int rowPitch)
    {
        for (int y = 0; y < fixture.Description.Height; y++)
        {
            for (int x = 0; x < fixture.Description.Width; x++)
            {
                var bytes = pixels.Slice(y * rowPitch + x * 8, 8);
                var actual = new Vector4((float)BitConverter.ToHalf(bytes), (float)BitConverter.ToHalf(bytes[2..]), (float)BitConverter.ToHalf(bytes[4..]), (float)BitConverter.ToHalf(bytes[6..]));
                var expected = fixture.Expected[y * (int)fixture.Description.Width + x];
                var delta = Vector4.Abs(actual - expected);
                if (!float.IsFinite(actual.X + actual.Y + actual.Z + actual.W) || Math.Max(Math.Max(delta.X, delta.Y), Math.Max(delta.Z, delta.W)) > .006f)
                { throw new InvalidOperationException($"Image filter pixel ({x},{y}): expected {expected}, actual {actual}."); }
            }
        }
    }
}
public sealed record ImageFilterFixture(GpuRenderGraphPlan Plan, GpuRenderGraphTexture Output, GpuGraphTextureDescription Description, Vector4[] Expected);
