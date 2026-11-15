namespace Lumyte.Graphics.Abstractions;

/// <summary>Selects the storage view for one registration, not resource capabilities.</summary>
public enum ShaderBufferAccess
{
    /// <summary>A read-only storage view.</summary>
    ReadOnly,

    /// <summary>A readable and writable storage view.</summary>
    ReadWrite,
}
