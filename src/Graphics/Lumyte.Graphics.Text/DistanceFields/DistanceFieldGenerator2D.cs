using Lumyte.Graphics.Drawing2D;
using Season.MSDF;
using Point = System.Numerics.Vector2;
using ShapePoint = Season.MSDF.Vector2;

namespace Lumyte.Graphics.Text;

/// <summary>Generates MSDF or MTSDF data from vector contours using the managed msdfgen pipeline.</summary>
public static class DistanceFieldGenerator2D
{
    private const int MaximumSegments = 4096;
    private const int MaximumPixels = 4 * 1024 * 1024;

    /// <summary>Colors analytic edges and generates a padded distance field with overlap handling and error correction.</summary>
    /// <param name="path">The immutable nonzero-filled logical outline; open contours are closed.</param>
    /// <param name="pixelsPerUnit">The finite positive source resolution per logical unit.</param>
    /// <param name="distanceRange">The finite positive full distance range in source texels.</param>
    /// <param name="mode">MSDF uses RGB distance and opaque alpha; MTSDF also stores true distance in alpha.</param>
    /// <param name="maximumDimension">The maximum generated width or height, between 1 and 4096.</param>
    /// <returns>Independent immutable CPU pixels; no GPU or operating system resources are used.</returns>
    public static DistanceFieldImage2D Generate(Path2D path, float pixelsPerUnit = 1, float distanceRange = 8, DistanceFieldMode2D mode = DistanceFieldMode2D.Mtsdf, int maximumDimension = 2048)
    {
        ArgumentNullException.ThrowIfNull(path);
        Positive(pixelsPerUnit, nameof(pixelsPerUnit));
        Positive(distanceRange, nameof(distanceRange));
        if (!float.IsFinite(1 / pixelsPerUnit))
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (maximumDimension is < 1 or > 4096)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumDimension));
        }

        if (path.Segments.Count > MaximumSegments)
        {
            throw new NotSupportedException("Distance-field outlines exceed the 4096 segment budget.");
        }

        Shape shape = ConvertPath(path);
        if (shape.EdgeCount() == 0)
        {
            byte[] empty = [0, 0, 0, mode == DistanceFieldMode2D.Msdf ? (byte)255 : (byte)0];
            return new DistanceFieldImage2D(1, 1, new Rect2D(0, 0, 1 / pixelsPerUnit, 1 / pixelsPerUnit), pixelsPerUnit, distanceRange, mode, empty);
        }

        shape.Normalize();
        if (!shape.Validate())
        {
            throw new ArgumentException("The outline does not form finite closed contours.", nameof(path));
        }

        Shape.Bounds bounds = shape.GetBounds();
        double padding = Math.Ceiling(distanceRange / 2d) + 1;
        double left = Math.Floor(bounds.L * pixelsPerUnit) - padding;
        double top = Math.Floor(-bounds.T * pixelsPerUnit) - padding;
        double right = Math.Ceiling(bounds.R * pixelsPerUnit) + padding;
        double bottom = Math.Ceiling(-bounds.B * pixelsPerUnit) + padding;
        double widthValue = right - left;
        double heightValue = bottom - top;
        if (!double.IsFinite(widthValue) || !double.IsFinite(heightValue) || widthValue < 1 || heightValue < 1 || widthValue > maximumDimension || heightValue > maximumDimension || widthValue * heightValue > MaximumPixels)
        {
            throw new NotSupportedException("The distance field exceeds the configured dimension or 4194304 pixel budget.");
        }

        int width = (int)widthValue;
        int height = (int)heightValue;
        var plane = new Rect2D((float)(left / pixelsPerUnit), (float)(top / pixelsPerUnit), width / pixelsPerUnit, height / pixelsPerUnit);
        if (!float.IsFinite(plane.X) || !float.IsFinite(plane.Y) || !float.IsFinite(plane.Width) || !float.IsFinite(plane.Height))
        {
            throw new ArgumentOutOfRangeException(nameof(pixelsPerUnit), "The logical placement rectangle must remain finite.");
        }

        EdgeColoring.EdgeColoringInkTrap(shape, 3, 0);
        int channels = mode == DistanceFieldMode2D.Mtsdf ? 4 : 3;
        var bitmap = new Bitmap(width, height, channels, YAxisOrientation.Y_DOWNWARD);
        var projection = new Projection(new ShapePoint(pixelsPerUnit, pixelsPerUnit), new ShapePoint(-left / pixelsPerUnit, bottom / pixelsPerUnit));
        var range = new Season.MSDF.Range(distanceRange / pixelsPerUnit);
        var config = new MSDFGeneratorConfig();
        if (mode == DistanceFieldMode2D.Mtsdf)
        {
            Msdfgen.GenerateMTSDF(bitmap.Section(), shape, projection, range, config);
            Rasterization.DistanceSignCorrection4(bitmap.Section(), shape, projection);
        }
        else
        {
            Msdfgen.GenerateMSDF(bitmap.Section(), shape, projection, range, config);
            Rasterization.DistanceSignCorrection3(bitmap.Section(), shape, projection);
        }

        float[] source = bitmap.GetRawData();
        byte[] result = new byte[checked(width * height * 4)];
        for (int pixel = 0; pixel < width * height; pixel++)
        {
            int input = pixel * channels;
            int output = pixel * 4;
            result[output] = Encode(source[input]);
            result[output + 1] = Encode(source[input + 1]);
            result[output + 2] = Encode(source[input + 2]);
            result[output + 3] = channels == 4 ? Encode(source[input + 3]) : (byte)255;
        }

        return new DistanceFieldImage2D(width, height, plane, pixelsPerUnit, distanceRange, mode, result);
    }

    private static Shape ConvertPath(Path2D path)
    {
        var shape = new Shape();
        Contour? contour = null;
        Point position = default;
        Point start = default;
        foreach (PathSegment2D segment in path.Segments)
        {
            switch (segment.Verb)
            {
                case PathVerb2D.Move:
                    Close();
                    position = start = segment.P0;
                    contour = shape.AddContour();
                    break;
                case PathVerb2D.Line:
                    EnsureContour();
                    if (position != segment.P0)
                    {
                        contour!.AddEdge(new EdgeHolder(Convert(position), Convert(segment.P0)));
                    }

                    position = segment.P0;
                    break;
                case PathVerb2D.Quadratic:
                    EnsureContour();
                    if (position != segment.P0 || position != segment.P1)
                    {
                        contour!.AddEdge(new EdgeHolder(Convert(position), Convert(segment.P0), Convert(segment.P1)));
                    }

                    position = segment.P1;
                    break;
                case PathVerb2D.Cubic:
                    EnsureContour();
                    if (position != segment.P0 || position != segment.P1 || position != segment.P2)
                    {
                        contour!.AddEdge(new EdgeHolder(Convert(position), Convert(segment.P0), Convert(segment.P1), Convert(segment.P2)));
                    }

                    position = segment.P2;
                    break;
                case PathVerb2D.Close:
                    Close();
                    position = start;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(path));
            }
        }

        Close();
        shape.Contours.RemoveAll(item => item.Edges.Count == 0);
        return shape;

        void Close()
        {
            if (contour is not null && contour.Edges.Count > 0 && position != start)
            {
                contour.AddEdge(new EdgeHolder(Convert(position), Convert(start)));
            }

            contour = null;
        }

        void EnsureContour()
        {
            if (contour is null)
            {
                start = position;
                contour = shape.AddContour();
            }
        }
    }

    private static ShapePoint Convert(Point point) => new(point.X, -point.Y);

    private static byte Encode(float value)
    {
        if (float.IsNaN(value))
        {
            throw new InvalidOperationException("The distance-field generator returned an undefined distance.");
        }

        // Smooth contours may use only two edge colors. Their absent channel has
        // infinite distance and must saturate, as it does in the msdfgen bitmap encoder.
        return (byte)Math.Clamp((int)MathF.Round(Math.Clamp(value, 0, 1) * 255), 0, 255);
    }

    private static void Positive(float value, string parameter)
    {
        if (!float.IsFinite(value) || value <= 0)
        {
            throw new ArgumentOutOfRangeException(parameter);
        }
    }
}
