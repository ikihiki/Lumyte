using Lumyte.Windowing.Abstractions;

namespace Lumyte.Windowing;

/// <summary>Defines HostedWindowDescriptor.</summary>
public sealed record HostedWindowDescriptor
{
    /// <summary>Gets Parent.</summary>
    public required WindowNode Parent { get; init; }

    /// <summary>Gets Bounds.</summary>
    public required WindowRect Bounds { get; init; }

    /// <summary>Gets Role.</summary>
    public WindowRole Role { get; init; } = WindowRole.Normal;

    /// <summary>Gets Activation.</summary>
    public WindowActivation Activation { get; init; } = WindowActivation.Activate;
}
