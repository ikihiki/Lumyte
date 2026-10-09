namespace Lumyte.Diagnostics;

/// <summary>The supported scalar types.</summary>
public enum DiagnosticValueKind
{
    /// <summary>A boolean.</summary>
    Boolean,

    /// <summary>A signed integer.</summary>
    Int64,

    /// <summary>A finite floating point value.</summary>
    Double,

    /// <summary>A non-null string.</summary>
    String,
}
