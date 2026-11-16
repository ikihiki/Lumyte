using Lumyte.Graphics.Text;

namespace Lumyte.UI.Media;

/// <summary>Creates independent immutable text snapshots using caller-owned font services.</summary>
public interface ITextLayoutProvider
{
    /// <summary>Synchronously lays out text at the final maximum body width.</summary>
    /// <param name="value">The non-null plain text.</param>
    /// <param name="maximumWidth">A nonnegative width or positive infinity; zero returns an empty zero-width snapshot.</param>
    /// <returns>A snapshot independent of the provider and its fonts.</returns>
    TextLayout2D CreateLayout(string value, float maximumWidth);
}
