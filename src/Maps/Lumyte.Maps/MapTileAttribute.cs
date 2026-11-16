namespace Lumyte.Maps;

/// <summary>A typed scalar attribute.</summary>
/// <param name="Name">Attribute name.</param>
/// <param name="Value">String, float, double, long, ulong or bool.</param>
public readonly record struct MapTileAttribute(string Name, object Value);
