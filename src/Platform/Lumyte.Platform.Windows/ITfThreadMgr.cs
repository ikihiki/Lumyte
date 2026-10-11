using System.Runtime.InteropServices;

namespace Lumyte.Platform.Windows;

/// <summary>Windows SDK ITfThreadMgr ABI declaration.</summary>
[ComImport]
[Guid("AA80E801-2021-11D2-93E0-0060B067B86E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfThreadMgr
{
    /// <summary>Calls the native Activate operation.</summary>
    /// <param name="client">The client argument.</param>
    void Activate(out uint client);

    /// <summary>Calls the native Deactivate operation.</summary>
    void Deactivate();

    /// <summary>Calls the native CreateDocumentMgr operation.</summary>
    /// <param name="document">The document argument.</param>
    void CreateDocumentMgr(out ITfDocumentMgr document);

    /// <summary>Calls the native EnumDocumentMgrs operation.</summary>
    /// <param name="enumerator">The enumerator argument.</param>
    void EnumDocumentMgrs(out nint enumerator);

    /// <summary>Calls the native GetFocus operation.</summary>
    /// <param name="document">The document argument.</param>
    void GetFocus(out ITfDocumentMgr? document);

    /// <summary>Calls the native SetFocus operation.</summary>
    /// <param name="document">The document argument.</param>
    void SetFocus(ITfDocumentMgr? document);

    /// <summary>Calls the native AssociateFocus operation.</summary>
    /// <param name="window">The window argument.</param>
    /// <param name="document">The document argument.</param>
    /// <param name="previous">The previous argument.</param>
    void AssociateFocus(nint window, ITfDocumentMgr? document, out ITfDocumentMgr? previous);

    /// <summary>Calls the native IsThreadFocus operation.</summary>
    /// <param name="focused">The focused argument.</param>
    void IsThreadFocus([MarshalAs(UnmanagedType.Bool)] out bool focused);

    /// <summary>Calls the native GetFunctionProvider operation.</summary>
    /// <param name="classId">The classId argument.</param>
    /// <param name="provider">The provider argument.</param>
    void GetFunctionProvider(ref Guid classId, out nint provider);

    /// <summary>Calls the native EnumFunctionProviders operation.</summary>
    /// <param name="enumerator">The enumerator argument.</param>
    void EnumFunctionProviders(out nint enumerator);

    /// <summary>Calls the native GetGlobalCompartment operation.</summary>
    /// <param name="compartments">The compartments argument.</param>
    void GetGlobalCompartment(out nint compartments);
}
