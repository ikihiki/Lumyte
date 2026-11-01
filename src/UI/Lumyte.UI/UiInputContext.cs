using System.Numerics;

namespace Lumyte.UI;

/// <summary>Provides synchronous input delivery and pointer capture for one receiving widget.</summary>
/// <remarks>Use only during OnInput. Do not retain this context for later work.</remarks>
public readonly struct UiInputContext
{
    private readonly UiSurface _surface;

    internal UiInputContext(UiSurface surface, UiElement receiver, UiInput input, bool isCaptured)
    {
        _surface = surface;
        Receiver = receiver;
        Input = input;
        IsCaptured = isCaptured;
    }

    /// <summary>Gets the widget currently receiving the input.</summary>
    public UiElement Receiver { get; }

    /// <summary>Gets the original logical input without converting keys into text.</summary>
    public UiInput Input { get; }

    /// <summary>Gets a value indicating whether this input was delivered through pointer capture, including terminal Up and Cancel.</summary>
    public bool IsCaptured { get; }

    /// <summary>Gets a value indicating whether a pointer is inside the receiver's prepared input region.</summary>
    public bool IsInside => Input is UiPointerInput pointer && _surface.IsInside(Receiver, pointer.Position);

    /// <summary>Gets the pointer position in receiver-local coordinates, or null for non-pointer input.</summary>
    public Vector2? PointerPosition => Input is UiPointerInput pointer ? pointer.Position - Receiver.InputOrigin : null;

    /// <summary>Attempts capture for this receiver during pointer Down or Move; returns false for an existing different owner or ineligible receiver.</summary>
    /// <returns>Whether this receiver owns the capture.</returns>
    public bool CapturePointer() => _surface.CapturePointer(Receiver, Input);

    /// <summary>Releases this receiver's capture and sends cancellation, without affecting another widget's capture.</summary>
    /// <returns>Whether a capture was released.</returns>
    public bool ReleasePointerCapture() => Input is UiPointerInput pointer && _surface.CancelPointer(pointer.PointerId, Receiver);
}
