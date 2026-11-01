using System.Numerics;
using Lumyte.Input.Abstractions;

namespace Lumyte.UI.Windowing;

/// <summary>A detached UiWindowSourceSnapshot value.</summary>
/// <param name="Revision">The Revision value.</param>
/// <param name="InputOrigin">The InputOrigin value.</param>
/// <param name="ClientSize">The ClientSize value.</param>
/// <param name="Scale">The Scale value.</param>
/// <param name="IsVisible">The IsVisible value.</param>
/// <param name="IsActive">The IsActive value.</param>
/// <param name="IsClosed">The IsClosed value.</param>
/// <param name="KeyboardScopeId">The KeyboardScopeId value.</param>
public sealed record UiWindowSourceSnapshot(ulong Revision, InputWindowId? InputOrigin, Vector2 ClientSize, Vector2 Scale, bool IsVisible, bool IsActive, bool IsClosed, Guid KeyboardScopeId);
