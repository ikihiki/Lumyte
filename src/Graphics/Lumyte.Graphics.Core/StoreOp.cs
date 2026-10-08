namespace Lumyte.Graphics;

/// <summary>
/// Selects attachment contents at the end of a render pass.
/// </summary>
public enum StoreOp
{
    /// <summary>
    /// Preserves the rendered contents.
    /// </summary>
    Store,

    /// <summary>
    /// Allows the rendered contents to be discarded.
    /// </summary>
    Discard,
}
