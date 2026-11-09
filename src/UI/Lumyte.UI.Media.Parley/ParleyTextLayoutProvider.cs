using Lumyte.Graphics.Text;
using Lumyte.Graphics.Text.Parley;

namespace Lumyte.UI.Media.Parley;

/// <summary>Shapes display-only text using a borrowed, explicitly configured Parley engine.</summary>
public sealed class ParleyTextLayoutProvider : ITextLayoutProvider
{
    private readonly ParleyTextEngine2D _engine;
    private readonly ParleyTextOptions2D _options;

    /// <summary>Initializes a new instance of the <see cref="ParleyTextLayoutProvider"/> class.</summary>
    /// <param name="engine">The borrowed engine; registration and disposal remain with its owner.</param>
    /// <param name="options">Paragraph options; all supplied lists are copied.</param>
    public ParleyTextLayoutProvider(ParleyTextEngine2D engine, ParleyTextOptions2D? options = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        _engine = engine;
        options ??= new();
        _options = options with
        {
            IncludeEditingGeometry = false,
            FallbackFontFamilies = options.FallbackFontFamilies.ToArray(),
            FontFeatures = options.FontFeatures.ToArray(),
            FontVariations = options.FontVariations.ToArray(),
            TextRuns = options.TextRuns.ToArray(),
        };
    }

    /// <inheritdoc/>
    public TextLayout2D CreateLayout(string value, float maximumWidth)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (float.IsNaN(maximumWidth) || maximumWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumWidth));
        }

        return maximumWidth == 0
            ? TextLayout2D.FromDrawings(value, default, default, 0, [])
            : _engine.CreateLayout(value, _options with { WrappingLength = maximumWidth });
    }
}
