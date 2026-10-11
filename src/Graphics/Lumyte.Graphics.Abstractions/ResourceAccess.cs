namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies ResourceAccess values.</summary>
[Flags]
public enum ResourceAccess
{
    /// <summary>Specifies None.</summary>
    None = 0,

    /// <summary>Specifies HostRead.</summary>
    HostRead = 1,

    /// <summary>Specifies HostWrite.</summary>
    HostWrite = 2,

    /// <summary>Specifies CopyRead.</summary>
    CopyRead = 4,

    /// <summary>Specifies CopyWrite.</summary>
    CopyWrite = 8,

    /// <summary>Specifies ShaderRead.</summary>
    ShaderRead = 64,

    /// <summary>Specifies ShaderWrite.</summary>
    ShaderWrite = 128,

    /// <summary>Specifies ColorRead.</summary>
    ColorRead = 256,

    /// <summary>Specifies ColorWrite.</summary>
    ColorWrite = 512,

    /// <summary>Allows index fetch.</summary>
    IndexRead = 16,

    /// <summary>Allows indirect command fetch.</summary>
    IndirectRead = 32,

    /// <summary>Allows depth/stencil attachment reads.</summary>
    DepthStencilRead = 1024,

    /// <summary>Allows depth/stencil attachment writes.</summary>
    DepthStencilWrite = 2048,
}
