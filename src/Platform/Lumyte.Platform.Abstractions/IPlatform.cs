using Lumyte.Input.Abstractions;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Abstractions;

/// <summary>Creates common services and owns the single OS event acquisition boundary.</summary>
/// <remarks>Use on the creating thread; dispose created services and windows before the platform.</remarks>
public interface IPlatform : IDisposable
{
    /// <summary>Gets the unique platform lifetime scope.</summary>
    Guid Id { get; }

    /// <summary>Gets implemented services; individual operations can still be denied.</summary>
    PlatformFeatures Features { get; }

    /// <summary>Creates a caller-owned, initially hidden native window.</summary>
    /// <param name="descriptor">The creation and ownership policy.</param>
    /// <returns>A window directly implemented by this backend.</returns>
    IWindow CreateWindow(WindowDescriptor descriptor);

    /// <summary>Creates the sole active acquisition source for this platform.</summary>
    /// <returns>A caller-owned input source without depending on InputSystem.</returns>
    IInputSource CreateInputSource();

    /// <summary>Creates a caller-owned Unicode clipboard service.</summary>
    /// <returns>The clipboard service.</returns>
    IClipboard CreateClipboard();

    /// <summary>Creates a caller-owned native text input service.</summary>
    /// <returns>The text service.</returns>
    ITextInputService CreateTextInputService();

    /// <summary>Acquires OS events once, updates native observations and queues normalized input.</summary>
    /// <remarks>Does not call WindowSystem.Update or InputSystem.Update. Reentrant pumping is forbidden.</remarks>
    void PumpEvents();
}
