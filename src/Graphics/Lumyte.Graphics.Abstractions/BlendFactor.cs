namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies BlendFactor values.</summary>
public enum BlendFactor
{
    /// <summary>Specifies Zero.</summary>
    Zero,

    /// <summary>Specifies One.</summary>
    One,

    /// <summary>Specifies SourceColor.</summary>
    SourceColor,

    /// <summary>Specifies OneMinusSourceColor.</summary>
    OneMinusSourceColor,

    /// <summary>Specifies SourceAlpha.</summary>
    SourceAlpha,

    /// <summary>Specifies OneMinusSourceAlpha.</summary>
    OneMinusSourceAlpha,

    /// <summary>Specifies DestinationColor.</summary>
    DestinationColor,

    /// <summary>Specifies OneMinusDestinationColor.</summary>
    OneMinusDestinationColor,

    /// <summary>Specifies DestinationAlpha.</summary>
    DestinationAlpha,

    /// <summary>Specifies OneMinusDestinationAlpha.</summary>
    OneMinusDestinationAlpha,

    /// <summary>Specifies SourceAlphaSaturated.</summary>
    SourceAlphaSaturated,

    /// <summary>Specifies Constant.</summary>
    Constant,

    /// <summary>Specifies OneMinusConstant.</summary>
    OneMinusConstant,
}
