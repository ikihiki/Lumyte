namespace Lumyte.Windowing.Abstractions;

/// <summary>Defines WindowDescriptor.</summary>
public sealed record WindowDescriptor
{
    /// <summary>Gets Title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Gets Kind.</summary>
    public WindowKind Kind { get; init; } = WindowKind.Normal;

    /// <summary>Gets Owner.</summary>
    public IWindow? Owner { get; init; }

    /// <summary>Gets ClientSize.</summary>
    /// <returns>The operation result.</returns>
    public System.Numerics.Vector2 ClientSize { get; init; } = new(800, 600);

    /// <summary>Gets Activation.</summary>
    public WindowActivation Activation { get; init; } = WindowActivation.Activate;

    /// <summary>Gets a value indicating whether IsResizable is enabled.</summary>
    public bool IsResizable { get; init; } = true;
}
