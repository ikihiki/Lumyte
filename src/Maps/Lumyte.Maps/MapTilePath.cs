using System.Collections.Immutable;

namespace Lumyte.Maps;

/// <summary>An immutable geometry path.</summary>
/// <param name="Points">Vertices, without repeated closing vertex.</param>
/// <param name="IsHole">Whether this ring belongs to the preceding exterior.</param>
public sealed record MapTilePath(ImmutableArray<MapPoint> Points, bool IsHole);
