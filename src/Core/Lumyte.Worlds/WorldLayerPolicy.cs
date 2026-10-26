namespace Lumyte.Worlds;

/// <summary>Controls which operations are blocked below a layer.</summary>
/// <param name="BlocksUpdateBelow">Whether to block updates of lower layers.</param>
/// <param name="BlocksDrawBelow">Whether to hide lower layers.</param>
/// <param name="BlocksInputBelow">Whether to block input to lower layers.</param>
public readonly record struct WorldLayerPolicy(bool BlocksUpdateBelow, bool BlocksDrawBelow, bool BlocksInputBelow);
