using System.Numerics;
using Lumyte.TextEditing;

namespace Lumyte.UI.Windowing.TextInput;

/// <summary>Defines ui ime candidate options.</summary>
public sealed record UiImeCandidateOptions
{
    /// <summary>Gets the policy.</summary>
    public ImeCandidateUiPolicy Policy { get; init; } = ImeCandidateUiPolicy.System;

    /// <summary>Gets the window policy.</summary>
    public UiWindowPolicy WindowPolicy { get; init; } = UiWindowPolicy.HostedOnly;

    /// <summary>Gets the client size.</summary>
    public Vector2 ClientSize { get; init; } = new(320, 240);
}
