using System.Collections.Immutable;

namespace Lumyte.Maps;

/// <summary>Immutable decoded CPU data; no pooled or borrowed input references.</summary>
/// <param name="Layers">Layers in input order.</param>
/// <param name="SourceRevision">Observed source snapshot revision.</param>
/// <param name="RetainedBytes">Accounted retained managed storage.</param>
/// <param name="PeakBuildBytes">Conservative cumulative build allocation plus input bytes.</param>
public sealed record MapTileContent(ImmutableArray<MapTileLayer> Layers, string SourceRevision, long RetainedBytes, long PeakBuildBytes);
