using System.Numerics;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Windowing;

/// <summary>Internal native preparation boundary.</summary>
internal interface IUiNativeWindowBackend
{
    /// <summary>Checks all required native capabilities.</summary>
    /// <param name="source">The source value.</param>
    /// <returns>The operation result.</returns>
    UiNativeWindowSupport GetSupport(IUiWindowSource source);

    /// <summary>Finds the anchor monitor client limit.</summary>
    /// <param name="source">The source value.</param>
    /// <param name="anchor">The anchor value.</param>
    /// <returns>The operation result.</returns>
    Vector2 GetLimit(IUiWindowSource source, Rect2D anchor);

    /// <summary>Creates an owned hidden native window.</summary>
    /// <param name="owner">The owner value.</param>
    /// <param name="request">The request value.</param>
    /// <returns>The operation result.</returns>
    IUiNativeWindow Create(IUiWindowSource owner, UiWindowCreateRequest request);
}

/// <summary>An owned native target with asynchronous request completion.</summary>
internal interface IUiNativeWindow
{
    /// <summary>Gets the actual native presentation source.</summary>
    IUiWindowSource Source { get; }

    /// <summary>Places beside the native owner anchor.</summary>
    /// <param name="anchor">The anchor value.</param>
    /// <param name="side">The side value.</param>
    /// <param name="gap">The gap value.</param>
    /// <returns>The operation result.</returns>
    ValueTask PlaceAsync(Rect2D anchor, UiWindowPlacementSide side, float gap);

    /// <summary>Shows while preserving keyboard focus.</summary>
    /// <returns>The operation result.</returns>
    ValueTask ShowAsync();

    /// <summary>Closes after all drawing and source loans retire.</summary>
    /// <returns>The operation result.</returns>
    ValueTask CloseAsync();
}
