using System.Collections.Immutable;

namespace Lumyte.Maps;

/// <summary>An immutable named layer with its own extent.</summary>
/// <param name="Name">Schema layer name.</param>
/// <param name="Extent">Positive tile extent.</param>
/// <param name="Features">Features in input order.</param>
public sealed record MapTileLayer(string Name, uint Extent, ImmutableArray<MapTileFeature> Features);
