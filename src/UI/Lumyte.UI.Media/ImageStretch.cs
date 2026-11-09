namespace Lumyte.UI.Media;

/// <summary>Specifies centered placement of an image inside its final body.</summary>
public enum ImageStretch
{
    /// <summary>Keep intrinsic logical dimensions.</summary>
    None,

    /// <summary>Stretch independently on both axes.</summary>
    Fill,

    /// <summary>Preserve aspect ratio and fit inside the body.</summary>
    Uniform,

    /// <summary>Preserve aspect ratio and cover the body, with overflow controlled by clipping.</summary>
    UniformToFill,
}
