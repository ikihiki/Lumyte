namespace Lumyte.Graphics.Abstractions;

/// <summary>Specifies permitted texture operations.</summary>
[Flags]
public enum TextureUsage
{
    /// <summary>Allows GPU copies to read the image.</summary>
    CopySource = 1,

    /// <summary>Allows GPU copies to write the image.</summary>
    CopyDestination = 2,

    /// <summary>Allows shader sampling through views.</summary>
    Sampled = 4,

    /// <summary>Allows use as a render attachment.</summary>
    RenderAttachment = 8,
}
