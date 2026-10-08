namespace Lumyte.Graphics;

/// <summary>
/// Selects attachment contents at the start of a render pass.
/// </summary>
public enum LoadOp
{
    /// <summary>
    /// Initializes the attachment with the supplied clear color.
    /// </summary>
    Clear,

    /// <summary>
    /// Preserves existing attachment contents.
    /// </summary>
    Load,
}
