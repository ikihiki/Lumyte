namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies StencilOperation values.</summary>
public enum StencilOperation
{
    /// <summary>Specifies Keep.</summary>
    Keep,

    /// <summary>Specifies Zero.</summary>
    Zero,

    /// <summary>Specifies Replace.</summary>
    Replace,

    /// <summary>Specifies IncrementClamp.</summary>
    IncrementClamp,

    /// <summary>Specifies DecrementClamp.</summary>
    DecrementClamp,

    /// <summary>Specifies Invert.</summary>
    Invert,

    /// <summary>Specifies IncrementWrap.</summary>
    IncrementWrap,

    /// <summary>Specifies DecrementWrap.</summary>
    DecrementWrap,
}
