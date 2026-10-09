namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies the comparison between the reference value and sampled depth.</summary>
public enum CompareFunction
{
    /// <summary>Never comparison.</summary>
    Never,

    /// <summary>Less comparison.</summary>
    Less,

    /// <summary>Equal comparison.</summary>
    Equal,

    /// <summary>LessOrEqual comparison.</summary>
    LessOrEqual,

    /// <summary>Greater comparison.</summary>
    Greater,

    /// <summary>NotEqual comparison.</summary>
    NotEqual,

    /// <summary>GreaterOrEqual comparison.</summary>
    GreaterOrEqual,

    /// <summary>Always comparison.</summary>
    Always,
}
