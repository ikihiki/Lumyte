using System.Runtime.InteropServices;

namespace Lumyte.Platform.Windows;

/// <summary>Windows SDK ITextStoreACPSink ABI declaration.</summary>
[ComImport]
[Guid("22D44C94-A419-4542-A272-AE26093ECECF")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITextStoreACPSink
{
    /// <summary>Calls the native OnTextChange operation.</summary>
    /// <param name="flags">The flags argument.</param>
    /// <param name="change">The change argument.</param>
    void OnTextChange(uint flags, ref TsTextChange change);

    /// <summary>Calls the native OnSelectionChange operation.</summary>
    void OnSelectionChange();

    /// <summary>Calls the native OnLayoutChange operation.</summary>
    /// <param name="code">The code argument.</param>
    /// <param name="view">The view argument.</param>
    void OnLayoutChange(uint code, uint view);

    /// <summary>Calls the native OnStatusChange operation.</summary>
    /// <param name="flags">The flags argument.</param>
    void OnStatusChange(uint flags);

    /// <summary>Calls the native OnAttrsChange operation.</summary>
    /// <param name="start">The start argument.</param>
    /// <param name="end">The end argument.</param>
    /// <param name="count">The count argument.</param>
    /// <param name="attributes">The attributes argument.</param>
    void OnAttrsChange(int start, int end, uint count, nint attributes);

    /// <summary>Calls the native OnLockGranted operation.</summary>
    /// <param name="flags">The flags argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int OnLockGranted(uint flags);

    /// <summary>Calls the native OnStartEditTransaction operation.</summary>
    void OnStartEditTransaction();

    /// <summary>Calls the native OnEndEditTransaction operation.</summary>
    void OnEndEditTransaction();
}
