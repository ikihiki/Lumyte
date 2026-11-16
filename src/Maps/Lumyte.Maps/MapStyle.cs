using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Maps;

/// <summary>Immutable schema-bound painter order.</summary>
public sealed class MapStyle
{
    /// <summary>Initializes a new instance of the <see cref="MapStyle"/> class.</summary>
    /// <param name="schemaId">Exact schema identifier.</param>
    /// <param name="version">Immutable style version.</param>
    /// <param name="background">Background paint.</param>
    /// <param name="layers">Ordered paint layers.</param>
    public MapStyle(string schemaId, string version, SRgba<float> background, IReadOnlyList<MapStyleLayer> layers)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentNullException.ThrowIfNull(layers);
        BackgroundPaint = PaintGraph2D.Solid(background);
        MapStyleLayer[] snapshot = layers.ToArray();
        if (snapshot.Any(layer => layer is null))
        {
            throw new ArgumentException("Layers cannot contain null.", nameof(layers));
        }

        SchemaId = schemaId;
        Version = version;
        Background = background;
        Layers = Array.AsReadOnly(snapshot);
    }

    /// <summary>Gets the schema.</summary>
    public string SchemaId { get; }

    /// <summary>Gets the immutable version.</summary>
    public string Version { get; }

    /// <summary>Gets the background.</summary>
    public SRgba<float> Background { get; }

    /// <summary>Gets painter order, including separate road casing and interior passes.</summary>
    public IReadOnlyList<MapStyleLayer> Layers { get; }

    internal PaintGraph2D BackgroundPaint { get; }
}
