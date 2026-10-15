using System.Numerics;

namespace Lumyte.Windowing.Abstractions;

/// <summary>A native presentation target implemented directly by an OS backend.</summary>
/// <remarks>Use on the platform's creating thread. Hosted regions are managed separately.</remarks>
public interface IWindow : IDisposable
{
    /// <summary>Occurs when the OS observation changes; the argument is an immutable snapshot.</summary>
    event Action<WindowState>? StateChanged;

    /// <summary>Occurs once per completed native request, including denied or unsupported requests.</summary>
    event Action<WindowRequestResult>? RequestCompleted;

    /// <summary>Occurs synchronously for a cancellable OS close request.</summary>
    /// <remarks>Only setting Cancel is allowed during this notification.</remarks>
    event EventHandler<WindowClosingEventArgs>? Closing;

    /// <summary>Gets the nonreused identifier scoped to the creating platform.</summary>
    WindowId Id { get; }

    /// <summary>Gets the immutable observed state, independently of pending requests.</summary>
    WindowState State { get; }

    /// <summary>Requests visibility using the configured activation policy.</summary>
    /// <returns>The native request identifier.</returns>
    WindowRequestId Show();

    /// <summary>Requests that the native target be hidden.</summary>
    /// <returns>The native request identifier.</returns>
    WindowRequestId Hide();

    /// <summary>Requests a title change.</summary>
    /// <param name="title">The nonnull title.</param>
    /// <returns>The native request identifier.</returns>
    WindowRequestId SetTitle(string title);

    /// <summary>Requests a positive, finite client size in logical pixels.</summary>
    /// <param name="clientSize">The requested logical client size.</param>
    /// <returns>The native request identifier.</returns>
    WindowRequestId Resize(Vector2 clientSize);

    /// <summary>Requests placement beside an anchor in the native owner's client coordinates.</summary>
    /// <param name="placement">The anchor, side and gap.</param>
    /// <returns>The native request identifier; unsupported backends report Unsupported.</returns>
    WindowRequestId Place(WindowPlacement placement);

    /// <summary>Requests activation, unless the target must preserve the existing focus.</summary>
    /// <returns>The native request identifier.</returns>
    WindowRequestId Activate();

    /// <summary>Requests native foreground ordering without changing logical hosted order.</summary>
    /// <returns>The native request identifier.</returns>
    WindowRequestId BringToFront();

    /// <summary>Requests a display mode supported by the backend's advertised capabilities.</summary>
    /// <param name="mode">The requested mode.</param>
    /// <returns>The native request identifier.</returns>
    WindowRequestId SetDisplayMode(WindowDisplayMode mode);

    /// <summary>Requests noncancellable closure, deferring physical destruction while retained.</summary>
    /// <returns>A request completed after physical closure, or denied while native children live.</returns>
    WindowRequestId Close();

    /// <summary>Retains an open native target while an external Surface or text context uses it.</summary>
    /// <returns>A caller-owned token, released after external resources are freed.</returns>
    /// <remarks>This does not synchronize GPU work or prevent forced OS resource loss.</remarks>
    IDisposable RetainNativeTarget();
}
