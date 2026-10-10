namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies TextureState values.</summary>
public enum TextureState
{
    /// <summary>Specifies Undefined.</summary>
    Undefined,

    /// <summary>Specifies CopySource.</summary>
    CopySource,

    /// <summary>Specifies CopyDestination.</summary>
    CopyDestination,

    /// <summary>Specifies Sampled.</summary>
    Sampled,

    /// <summary>Specifies ColorAttachment.</summary>
    ColorAttachment,

    /// <summary>Allows depth/stencil attachment reads and writes.</summary>
    DepthStencilAttachment,
}
