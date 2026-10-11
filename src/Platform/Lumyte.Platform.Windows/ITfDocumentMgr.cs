using System.Runtime.InteropServices;

namespace Lumyte.Platform.Windows;

/// <summary>Windows SDK ITfDocumentMgr ABI declaration.</summary>
[ComImport]
[Guid("AA80E7F4-2021-11D2-93E0-0060B067B86E")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITfDocumentMgr
{
    /// <summary>Calls the native CreateContext operation.</summary>
    /// <param name="client">The client argument.</param>
    /// <param name="flags">The flags argument.</param>
    /// <param name="store">The store argument.</param>
    /// <param name="context">The context argument.</param>
    /// <param name="cookie">The cookie argument.</param>
    void CreateContext(uint client, uint flags, [MarshalAs(UnmanagedType.IUnknown)] object store, [MarshalAs(UnmanagedType.IUnknown)] out object context, out uint cookie);

    /// <summary>Calls the native Push operation.</summary>
    /// <param name="context">The context argument.</param>
    void Push([MarshalAs(UnmanagedType.IUnknown)] object context);

    /// <summary>Calls the native Pop operation.</summary>
    /// <param name="flags">The flags argument.</param>
    void Pop(uint flags);

    /// <summary>Calls the native GetTop operation.</summary>
    /// <param name="context">The context argument.</param>
    void GetTop(out nint context);

    /// <summary>Calls the native GetBase operation.</summary>
    /// <param name="context">The context argument.</param>
    void GetBase(out nint context);

    /// <summary>Calls the native EnumContexts operation.</summary>
    /// <param name="enumerator">The enumerator argument.</param>
    void EnumContexts(out nint enumerator);
}
