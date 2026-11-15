using System.Linq.Expressions;
using System.Numerics;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;

// Delegate binding is setup-only; internal access does not change production API.
public class ClipCpuBenchmarks
{
    [Params("cold-triangle", "cold-128-edges", "equivalent200", "novel200")]
    public string Case { get; set; } = "cold-triangle";
    Func<IReadOnlyList<ClipElement2D>, DrawingTargetInfo2D, bool, object> create = null!;
    Func<DrawingTargetInfo2D, object> cache = null!;
    Func<object, object, Matrix3x2, IReadOnlyList<ClipElement2D>, bool, object> get = null!;
    readonly DrawingTargetInfo2D target = new() { Description = new() { Width = 64, Height = 64, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment } };
    ClipElement2D[][] elements = null!;
    readonly object[] keys = Enumerable.Range(0, 200).Select(_ => new object()).ToArray();
    long state;

    [GlobalSetup]
    public void Setup()
    {
        var assembly = typeof(Canvas2D).Assembly;
        var type = assembly.GetType("Lumyte.Graphics.Drawing2D.NormalizedClip2D")!;
        var e = Expression.Parameter(typeof(IReadOnlyList<ClipElement2D>));
        var t = Expression.Parameter(typeof(DrawingTargetInfo2D));
        var p = Expression.Parameter(typeof(bool));
        create = Expression.Lambda<Func<IReadOnlyList<ClipElement2D>, DrawingTargetInfo2D, bool, object>>(Expression.Convert(Expression.Call(type.GetMethod("Create", BindingFlags.Static | BindingFlags.NonPublic)!, e, t, p), typeof(object)), e, t, p).Compile();
        var ct = assembly.GetType("Lumyte.Graphics.Drawing2D.ClipNormalizationCache2D")!;
        cache = Expression.Lambda<Func<DrawingTargetInfo2D, object>>(Expression.Convert(Expression.New(ct.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Single(), t), typeof(object)), t).Compile();
        var c = Expression.Parameter(typeof(object)); var k = Expression.Parameter(typeof(object)); var m = Expression.Parameter(typeof(Matrix3x2));
        get = Expression.Lambda<Func<object, object, Matrix3x2, IReadOnlyList<ClipElement2D>, bool, object>>(Expression.Convert(Expression.Call(Expression.Convert(c, ct), ct.GetMethod("Get", BindingFlags.Instance | BindingFlags.NonPublic)!, k, m, e, p), typeof(object)), c, k, m, e, p).Compile();
        elements = Enumerable.Range(0, 200).Select(i =>
        {
            var b = new PathBuilder2D(); b.MoveTo(new(0.125f, 0.125f));
            if (Case == "cold-128-edges")
                for (int j = 0; j < 128; j++) { double a = j * Math.PI * 2 / 128; b.LineTo(new(30 + (float)Math.Cos(a) * 28, 30 + (float)Math.Sin(a) * 28)); }
            else { b.LineTo(new(48.625f, 0.375f)); b.LineTo(new(0.375f, 48.625f)); }
            b.Close();
            return new[] { new ClipElement2D(b.Build(), Matrix3x2.Identity, FillRule2D.NonZero) };
        }).ToArray();
    }

    [Benchmark]
    public object Normalize()
    {
        // Every invocation creates fresh masks, including recurring benchmark states.
        if (Case.StartsWith("cold")) return create(elements[0], target, false);
        object scope = cache(target), result = scope;
        state++;
        for (int i = 0; i < 200; i++)
        {
            IReadOnlyList<ClipElement2D> input = elements[i];
            if (Case == "novel200") input = new[] { elements[i][0] with { Transform = Matrix3x2.CreateTranslation((float)((state % 65536) * 0.000001 + i * 0.0001), 0) } };
            result = get(scope, keys[i], Matrix3x2.Identity, input, false);
        }
        return result;
    }
}
