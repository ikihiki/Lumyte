namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies the descriptor uses of a raw buffer reference within one draw.</summary>
[Flags]
public enum ArgumentBufferAccess
{
    /// <summary>Uses a read-only storage binding.</summary>
    Read = 1,

    /// <summary>Uses a writable storage binding.</summary>
    Write = 2,

    /// <summary>Uses both binding classes, without merging their physical budgets.</summary>
    ReadWrite = Read | Write,
}
