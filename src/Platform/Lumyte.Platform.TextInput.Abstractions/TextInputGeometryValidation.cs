using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.TextInput.Abstractions;

/// <summary>Defines text input geometry validation.</summary>
public static class TextInputGeometryValidation
{
    /// <summary>Copy.</summary>
    /// <param name = "geometry">The geometry.</param>
    /// <returns>The operation result.</returns>
    public static TextInputGeometry Copy(TextInputGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        Check(geometry.Viewport);
        Check(geometry.Caret);
        foreach (TextInputRangeGeometry range in geometry.Ranges)
        {
            if (range.Range.Start < 0 || range.Range.Length < 0)
            {
                throw new ArgumentException("Invalid text range.", nameof(geometry));
            }

            Check(range.Bounds);
        }

        foreach (TextInputCaretGeometry caret in geometry.CaretStops)
        {
            if (caret.Position < 0)
            {
                throw new ArgumentException("Invalid caret position.", nameof(geometry));
            }

            Check(caret.Bounds);
        }

        return geometry with
        {
            Ranges = Array.AsReadOnly(geometry.Ranges.ToArray()),
            CaretStops = Array.AsReadOnly(geometry.CaretStops.ToArray()),
        };
    }

    private static void Check(WindowRect rect)
    {
        if (!double.IsFinite(rect.X) || !double.IsFinite(rect.Y) || !double.IsFinite(rect.Width) || !double.IsFinite(rect.Height) || rect.Width < 0 || rect.Height < 0)
        {
            throw new ArgumentException("Text geometry must be finite and nonnegative.", nameof(rect));
        }
    }
}
