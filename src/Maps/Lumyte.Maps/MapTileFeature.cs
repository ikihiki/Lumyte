using System.Collections.Immutable;

namespace Lumyte.Maps;

/// <summary>An immutable feature.</summary>
/// <param name="Id">Optional source feature ID.</param>
/// <param name="GeometryType">Geometry kind.</param>
/// <param name="Paths">Ordered paths preserving multipolygon grouping.</param>
/// <param name="Attributes">Typed attributes.</param>
public sealed record MapTileFeature(ulong? Id, MapGeometryType GeometryType, ImmutableArray<MapTilePath> Paths, ImmutableArray<MapTileAttribute> Attributes);
