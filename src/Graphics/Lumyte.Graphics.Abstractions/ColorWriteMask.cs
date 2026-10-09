namespace Lumyte.Graphics.Abstractions;

/// <summary>Selects writable color channels.</summary>
[Flags]
public enum ColorWriteMask
{
    /// <summary>Specifies None.</summary>
    None = 0,

    /// <summary>Specifies Red.</summary>
    Red = 1,

    /// <summary>Specifies Green.</summary>
    Green = 2,

    /// <summary>Specifies Blue.</summary>
    Blue = 4,

    /// <summary>Specifies Alpha.</summary>
    Alpha = 8,

    /// <summary>Specifies All.</summary>
    All = 15,
}
