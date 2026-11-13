using System.Numerics;
using Lumyte.Mathematics.Colors;
using Lumyte.Mathematics.Geometry;

namespace Lumyte.Mathematics.Sample;

internal static class Program
{
    private static void Main()
    {
        Require(new SRgba8(255, 0, 0) == CssColors.Red, "Named colors are byte codes, not unit components");
        Require(CssColors.CornflowerBlue.ToNormalized<double>().R == 100d / 255, "Palette explicit normalization");
        SRgba<float> paletteCodes = CssColors.Red.ToCodes().ConvertChecked<float>();
        SRgba<float> paletteUnit = CssColors.Red;
        Require(paletteCodes.R == 255f && paletteCodes.A == 255f, "Numeric cast preserves raw codes");
        Require(paletteUnit == new SRgba<float>(1, 0, 0, 1), "SRgba8 implicitly becomes unit straight sRGB");
        Require(new SRgba<float>(-2, 4, 0, 3).WithAlpha(2).A == 2, "Common storage retains finite raw/HDR and nonunit alpha");
        Require(CssColors.Transparent == default, "CSS transparent is black with zero alpha");
        Require(CssColors.TryGet("rebeccapurple", out SRgba8 rebecca) && rebecca.ToCodes().ToArgb32Codes() == 0xFF663399, "Named palette lookup");
        Require(CssColors.All.Count == 149 && !CssColors.TryGet("currentColor", out _), "Fixed keywords only, no contextual theme values");
        Require(SBgra<byte>.FromRgba(CssColors.Red.ToCodes()).ToRgba() == CssColors.Red.ToCodes(), "Palette raw layout conversion");
        Require(SGray<float>.FromRgba(CssColors.Gray).Value == 128 / 255f, "Palette normalized grayscale layout");
        Require(CssColors.CornflowerBlue.ToCodes().ConvertChecked<ManagedDecimal>().NormalizeUnorm<decimal>(new(255)).R == 100m / 255, "Palette custom numeric conversion");
        var colorCodes = SRgba<byte>.FromArgb32Codes(0x80FF0000u);
        SRgba<double> normalizedColor = colorCodes.NormalizeUnorm<double>(byte.MaxValue);
        Require(normalizedColor.R == 1 && normalizedColor.A == 128d / 255, "Explicit code normalization");
        LRgba<double> linearColor = normalizedColor.ToLinear<double>().PremultiplyLinear<double>();
        Require(linearColor.R == normalizedColor.A, "Transfer and association");
        SRgba<double> encodedAgain = linearColor.UnpremultiplyLinear<double>().ToSrgb<double>();
        double expectedEncodedOne = (1.055 * Math.Pow(1, 1d / 2.4)) - 0.055;
        Require(encodedAgain.R == expectedEncodedOne && encodedAgain.A == normalizedColor.A, "Explicit unassociation and retained approximate encoding");
        var linearManaged = new LGray<ManagedDecimal>(new(128));
        Require(linearManaged.NormalizeUnorm<decimal>(new(255)).R == 128m / 255, "Custom linear numeric storage");
        var cyan = SRgba<double>.FromHsvSrgbDegrees(180m, 1m, 1m, 1m);
        Require(cyan == new SRgba<double>(0, 1, 1, 1), "Exact HSV color factory");
        byte[] pixel = new byte[4];
        SBgra<double>.FromRgba(cyan).WritePixel(pixel, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
        var decodedPixels = new SBgra<double>[1];
        Lumyte.Mathematics.Colors.Color.DecodePixels<SBgra<double>>(pixel, decodedPixels, ColorComponentEncoding.Unorm8, ColorByteOrder.LittleEndian);
        Require(decodedPixels[0].ToRgba() == cyan, "Typed batch pixel API");
        var grayCodes = new SGray<byte>(128);
        Require(grayCodes.ToRgba((byte)255) == new SRgba<byte>(128, 128, 128, 255), "Explicit opaque raw alpha");
        Require(grayCodes.NormalizeUnorm<double>(255) == new SRgba<double>(128d / 255, 128d / 255, 128d / 255, 1), "Gray byte normalization");
        Require(new SGray<double>(0.5).ToRgba(1) == new SRgba<double>(0.5, 0.5, 0.5, 1), "Gray double explicit alpha");
        Require(new SGrayAlpha<decimal>(0.25m, 0.5m).ToRgba() == new SRgba<decimal>(0.25m, 0.25m, 0.25m, 0.5m), "Gray alpha components");
        Require(new SRgb<int>(1, 2, 3).ToRgba(255).A == 255, "RGB does not guess opaque code");
        var packed = RawRgb565.FromSrgb(cyan);
        Require(RawRgb565.FromLinear(cyan.ToLinear<double>()).InterpretLinear<double>().G == 1, "Explicit linear packed interpretation");
        Require(packed.InterpretSrgb<double>() == cyan, "Concrete RGB565 value");
        byte[] packedBytes = new byte[2];
        Lumyte.Mathematics.Colors.Color.WritePackedPixels<RawRgb565>(new[] { packed }, packedBytes, ColorByteOrder.BigEndian);
        var packedCopy = new RawRgb565[1];
        Lumyte.Mathematics.Colors.Color.ReadPackedPixels<RawRgb565>(packedBytes, packedCopy, ColorByteOrder.BigEndian);
        Require(packedCopy[0] == packed, "Typed packed batch");
        Require(new SHsla<decimal>(180, 1, 0.5m, 1).ToRgba<double>() == cyan, "HSLA value");
        Require(new SHsva<decimal>(180, 1, 1, 1).ToRgba<double>() == cyan, "HSVA value");
        var managedGray = new SGray<ManagedDecimal>(new(128));
        Require(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<SGray<ManagedDecimal>>(), "Reference-containing custom layout");
        Require(managedGray.NormalizeUnorm<decimal>(new(255)).R == 128m / 255, "Custom INumber exact normalization");
        var colorMaximum = BigInteger.Pow(10, 400);
        Require(new SRgba<BigInteger>(0, colorMaximum, 0, colorMaximum).NormalizeUnorm<float>(colorMaximum).G == 1, "Huge exact UNORM maximum");

        // This ordinary project reference deliberately has no friend-assembly access or test dependencies.
        CheckPolygon<int>(100);
        CheckPolygon<decimal>(100m);
        CheckPolygon<BigInteger>(BigInteger.Pow(10, 400));

        Point2D<decimal> translated = Geometry2D.TransformAffineChecked(new Point2D<decimal>(1.25m, 2.5m), 1m, 0m, 0m, 1m, 3m, 4m);
        Require(translated == new Point2D<decimal>(4.25m, 6.5m), "Native decimal affine coefficients");
        Point2D<double> quadratic = Geometry2D.EvaluateQuadraticBezier<int, double>(new(0, 0), new(0, 2), new(2, 0), 0.5);
        Require(quadratic == new Point2D<double>(0.5, 1), "Explicit fractional curve result");

        CountingSink sink = default;
        Geometry2D.AppendFlattenedQuadraticBcl(Vector2.Zero, new Vector2(1, 2), new Vector2(2, 0), 0.01f, ref sink, 12);
        Geometry2D.AppendFlattenedCubicBcl(Vector2.Zero, new Vector2(0, 2), new Vector2(2, 2), new Vector2(2, 0), 0.01f, ref sink, 12);
        Require(sink.Count > 4 && sink.Last == new Vector2(2, 0), "Public constrained streaming struct sink");

        float[] x = Enumerable.Range(0, 257).Select(value => (float)value).ToArray();
        float[] y = Enumerable.Repeat(2f, x.Length).ToArray();
        float[] squared = new float[x.Length];
        Geometry2D.SquaredDistancesBcl(x, y, 1, 2, squared);
        for (int i = 0; i < squared.Length; i++)
        {
            Require(squared[i] == (i - 1) * (i - 1), "Float span batch including SIMD tail");
        }

        double[] xd = Array.ConvertAll(x, value => (double)value);
        double[] yd = Array.ConvertAll(y, value => (double)value);
        double[] squaredDouble = new double[x.Length];
        Geometry2D.SquaredDistancesBcl(xd, yd, 1, 2, squaredDouble);
        Require(squaredDouble[256] == 65025, "Double span batch");

        // BCL intentionally rounds the input to float; the generic double path does not.
        var precise = new Point2D<double>(16777217, 0);
        Point2D<double> fused = Geometry2D.TransformAffineFused(precise, Matrix3x2.Identity);
        var bcl = Vector2.Transform(new Vector2((float)precise.X, 0), Matrix3x2.Identity);
        Require(fused.X == 16777217 && bcl.X == 16777216, "Double FMA versus float BCL precision");
        Require(Geometry2D.DistanceToSegmentBcl(new Vector2(1, 1), Vector2.Zero, new Vector2(2, 0)) == 1, "BCL segment metric");
        Geometry2D.GetBoundsBcl(Vector2.Zero, Vector2.UnitX, Vector2.UnitY, Vector2.One, out Vector2 minimum, out Vector2 maximum);
        Require(minimum == Vector2.Zero && maximum == Vector2.One, "BCL bounds reduction");

        var origin = new Point3D<int>(0, 0, 0);
        var endpoint = new Point3D<int>(2, 3, 6);
        Require(Geometry3D.Distance<int, double>(origin, endpoint) == 7, "3D explicit-result distance");
        Require(Geometry3D.DistanceSquared<int, decimal>(origin, endpoint) == 49m, "3D decimal squared distance");
        Point3D<double> closest = Geometry3D.ClosestPointOnSegment<int, double>(new Segment3D<int>(origin, new Point3D<int>(2, 0, 0)), new Point3D<int>(1, 2, 0));
        Require(closest == new Point3D<double>(1, 0, 0), "3D explicit-result projection");
        AxisAlignedBox3D<int> bounds = Geometry3D.GetBounds<int>(new[] { origin, endpoint });
        Require(bounds.Contains(new Point3D<int>(1, 2, 3)), "3D closed bounds reduction");
        Console.WriteLine("Public mathematics consumer: all assertions passed (typed color layouts, packed words, HSL/HSV, custom numbers; geometry, spans, BCL/FMA, 3D).");
    }

    private static void CheckPolygon<T>(T size)
        where T : INumber<T>
    {
        Point2D<T>[] square = [new(T.Zero, T.Zero), new(size, T.Zero), new(size, size), new(T.Zero, size)];
        Require(Geometry2D.ContainsPolygonClosed<T>(square, new(T.One, T.One)), "Exact numeric polygon interior");
        Require(Geometry2D.ContainsPolygonClosed<T>(square, new(size, T.One)), "Closed polygon boundary");
        Require(!Geometry2D.ContainsPolygonClosed<T>(square, new(-T.One, T.One)), "Exact numeric polygon exterior");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private struct CountingSink : Geometry2D.IStreamingPointSink
    {
        public int Count;

        public Vector2 Last;

        public void Append(Vector2 point)
        {
            Count++;
            Last = point;
        }
    }
}
