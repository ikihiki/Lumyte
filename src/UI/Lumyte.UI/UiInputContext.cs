using System.Numerics;

namespace Lumyte.UI;

/// <summary>Provides synchronous input delivery, capture and focus requests for one receiving element.</summary>
/// <remarks>Use only during OnInput. Do not retain this context for later work.</remarks>
public readonly struct UiInputContext
{
    private readonly UiSurface _surface;

    internal UiInputContext(UiSurface surface, UiElement receiver, UiInput input, bool isCaptured, bool isKeyCaptured = false)
    {
        _surface = surface;
        Receiver = receiver;
        Input = input;
        IsCaptured = isCaptured;
        IsKeyCaptured = isKeyCaptured;
    }

    /// <summary>Gets the element currently receiving the input.</summary>
    public UiElement Receiver { get; }

    /// <summary>Gets the original logical input without converting keys into text.</summary>
    public UiInput Input { get; }

    /// <summary>Gets a value indicating whether this input was delivered through pointer capture, including terminal Up and Cancel.</summary>
    public bool IsCaptured { get; }

    /// <summary>Gets a value indicating whether this input was delivered through key capture, including terminal Up and Cancel.</summary>
    public bool IsKeyCaptured { get; }

    /// <summary>Gets a value indicating whether this receiver currently has surface-local focus.</summary>
    public bool IsFocused => _surface.IsFocused(Receiver);

    /// <summary>Gets a value indicating whether a pointer is inside the receiver's prepared input region.</summary>
    public bool IsInside => Input is UiPointerInput pointer && _surface.IsInside(Receiver, pointer.Position);

    /// <summary>Gets the pointer position in receiver-local coordinates, or null for non-pointer input.</summary>
    public Vector2? PointerPosition => Input is UiPointerInput pointer ? pointer.Position - Receiver.InputOrigin : null;

    /// <summary>Attempts capture for this receiver during pointer Down or Move; returns false for an existing different owner or ineligible receiver.</summary>
    /// <returns>Whether this receiver owns the capture.</returns>
    public bool CapturePointer() => _surface.CapturePointer(Receiver, Input);

    /// <summary>Releases this receiver's capture and sends cancellation, without affecting another element's capture.</summary>
    /// <returns>Whether a capture was released.</returns>
    public bool ReleasePointerCapture() => Input is UiPointerInput pointer && _surface.CancelPointer(pointer.PointerId, Receiver);

    /// <summary>Requests focus for the eligible receiver without updating layout; a disconnected or disposed receiver returns false.</summary>
    /// <returns>Whether this receiver could be focused.</returns>
    public bool RequestFocus() => _surface.RequestFocus(Receiver);

    /// <summary>Attempts capture during key Down; an orphan repeat cannot begin capture.</summary>
    /// <returns>Whether the receiver owns this key series.</returns>
    public bool CaptureKey() => _surface.CaptureKey(Receiver, Input);

    /// <summary>Releases this receiver's key capture, notifying cancellation and retaining consumption until Up.</summary>
    /// <returns>Whether a capture was released.</returns>
    public bool ReleaseKeyCapture() => Input is UiKeyInput key && _surface.CancelKey(key.Key, Receiver);
}
