using System.Runtime.InteropServices;

namespace Lumyte.Platform.Windows;

/// <summary>Windows SDK ITfContextOwnerCompositionSink ABI declaration.</summary>
[ComVisible(true)]
[Guid("5F20AA40-B57A-4F34-96AB-3576F377CC79")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfContextOwnerCompositionSink
{
    /// <summary>Calls the native OnStartComposition operation.</summary>
    /// <param name="composition">The composition argument.</param>
    /// <param name="accepted">The accepted argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int OnStartComposition(nint composition, [MarshalAs(UnmanagedType.Bool)] out bool accepted);

    /// <summary>Calls the native OnUpdateComposition operation.</summary>
    /// <param name="composition">The composition argument.</param>
    /// <param name="range">The range argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int OnUpdateComposition(nint composition, nint range);

    /// <summary>Calls the native OnEndComposition operation.</summary>
    /// <param name="composition">The composition argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int OnEndComposition(nint composition);
}
