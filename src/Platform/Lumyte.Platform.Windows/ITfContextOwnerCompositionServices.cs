using System.Runtime.InteropServices;

namespace Lumyte.Platform.Windows;

/// <summary>Windows SDK ITfContextOwnerCompositionServices ABI declaration.</summary>
[ComImport]
[Guid("86462810-593B-4916-9764-19C08E9CE110")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfContextOwnerCompositionServices
{
    /// <summary>Calls the native StartComposition operation.</summary>
    /// <param name="cookie">The cookie argument.</param>
    /// <param name="range">The range argument.</param>
    /// <param name="sink">The sink argument.</param>
    /// <param name="composition">The composition argument.</param>
    void StartComposition(uint cookie, nint range, nint sink, out nint composition);

    /// <summary>Calls the native EnumCompositions operation.</summary>
    /// <param name="enumerator">The enumerator argument.</param>
    void EnumCompositions(out nint enumerator);

    /// <summary>Calls the native FindComposition operation.</summary>
    /// <param name="cookie">The cookie argument.</param>
    /// <param name="range">The range argument.</param>
    /// <param name="enumerator">The enumerator argument.</param>
    void FindComposition(uint cookie, nint range, out nint enumerator);

    /// <summary>Calls the native TakeOwnership operation.</summary>
    /// <param name="cookie">The cookie argument.</param>
    /// <param name="view">The view argument.</param>
    /// <param name="sink">The sink argument.</param>
    /// <param name="composition">The composition argument.</param>
    void TakeOwnership(uint cookie, nint view, nint sink, out nint composition);

    /// <summary>Calls the native TerminateComposition operation.</summary>
    /// <param name="view">The view argument.</param>
    void TerminateComposition(nint view);
}
