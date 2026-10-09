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
}
