using System.Runtime.InteropServices;

namespace Lumyte.Platform.Windows;

/// <summary>Windows SDK ITextStoreACP ABI declaration.</summary>
[ComVisible(true)]
[Guid("28888FE3-C2A0-483A-A3EA-8CB1CE51FF3D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITextStoreACP
{
    /// <summary>Calls the native AdviseSink operation.</summary>
    /// <param name="interfaceId">The interfaceId argument.</param>
    /// <param name="sink">The sink argument.</param>
    /// <param name="mask">The mask argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int AdviseSink(ref Guid interfaceId, [MarshalAs(UnmanagedType.IUnknown)] object sink, uint mask);

    /// <summary>Calls the native UnadviseSink operation.</summary>
    /// <param name="sink">The sink argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int UnadviseSink([MarshalAs(UnmanagedType.IUnknown)] object sink);

    /// <summary>Calls the native RequestLock operation.</summary>
    /// <param name="flags">The flags argument.</param>
    /// <param name="sessionResult">The sessionResult argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int RequestLock(uint flags, out int sessionResult);

    /// <summary>Calls the native GetStatus operation.</summary>
    /// <param name="status">The status argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int GetStatus(out TsStatus status);

    /// <summary>Calls the native QueryInsert operation.</summary>
    /// <param name="start">The start argument.</param>
    /// <param name="end">The end argument.</param>
    /// <param name="count">The count argument.</param>
    /// <param name="resultStart">The resultStart argument.</param>
    /// <param name="resultEnd">The resultEnd argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int QueryInsert(int start, int end, uint count, out int resultStart, out int resultEnd);

    /// <summary>Calls the native GetSelection operation.</summary>
    /// <param name="index">The index argument.</param>
    /// <param name="count">The count argument.</param>
    /// <param name="selections">The selections argument.</param>
    /// <param name="fetched">The fetched argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int GetSelection(uint index, uint count, nint selections, out uint fetched);

    /// <summary>Calls the native SetSelection operation.</summary>
    /// <param name="count">The count argument.</param>
    /// <param name="selections">The selections argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int SetSelection(uint count, nint selections);

    /// <summary>Calls the native GetText operation.</summary>
    /// <param name="start">The start argument.</param>
    /// <param name="end">The end argument.</param>
    /// <param name="text">The text argument.</param>
    /// <param name="textCapacity">The textCapacity argument.</param>
    /// <param name="textLength">The textLength argument.</param>
    /// <param name="runs">The runs argument.</param>
    /// <param name="runCapacity">The runCapacity argument.</param>
    /// <param name="runCount">The runCount argument.</param>
    /// <param name="next">The next argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int GetText(int start, int end, nint text, uint textCapacity, out uint textLength, nint runs, uint runCapacity, out uint runCount, out int next);

    /// <summary>Calls the native SetText operation.</summary>
    /// <param name="flags">The flags argument.</param>
    /// <param name="start">The start argument.</param>
    /// <param name="end">The end argument.</param>
    /// <param name="text">The text argument.</param>
    /// <param name="count">The count argument.</param>
    /// <param name="change">The change argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int SetText(uint flags, int start, int end, nint text, uint count, out TsTextChange change);

    /// <summary>Calls the native GetFormattedText operation.</summary>
    /// <param name="start">The start argument.</param>
    /// <param name="end">The end argument.</param>
    /// <param name="data">The data argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int GetFormattedText(int start, int end, out nint data);

    /// <summary>Calls the native GetEmbedded operation.</summary>
    /// <param name="position">The position argument.</param>
    /// <param name="service">The service argument.</param>
    /// <param name="interfaceId">The interfaceId argument.</param>
    /// <param name="value">The value argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int GetEmbedded(int position, ref Guid service, ref Guid interfaceId, out nint value);

    /// <summary>Calls the native QueryInsertEmbedded operation.</summary>
    /// <param name="service">The service argument.</param>
    /// <param name="format">The format argument.</param>
    /// <param name="insertable">The insertable argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int QueryInsertEmbedded(nint service, nint format, [MarshalAs(UnmanagedType.Bool)] out bool insertable);

    /// <summary>Calls the native InsertEmbedded operation.</summary>
    /// <param name="flags">The flags argument.</param>
    /// <param name="start">The start argument.</param>
    /// <param name="end">The end argument.</param>
    /// <param name="data">The data argument.</param>
    /// <param name="change">The change argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int InsertEmbedded(uint flags, int start, int end, nint data, out TsTextChange change);

    /// <summary>Calls the native InsertTextAtSelection operation.</summary>
    /// <param name="flags">The flags argument.</param>
    /// <param name="text">The text argument.</param>
    /// <param name="count">The count argument.</param>
    /// <param name="start">The start argument.</param>
    /// <param name="end">The end argument.</param>
    /// <param name="change">The change argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int InsertTextAtSelection(uint flags, nint text, uint count, out int start, out int end, out TsTextChange change);

    /// <summary>Calls the native InsertEmbeddedAtSelection operation.</summary>
    /// <param name="flags">The flags argument.</param>
    /// <param name="data">The data argument.</param>
    /// <param name="start">The start argument.</param>
    /// <param name="end">The end argument.</param>
    /// <param name="change">The change argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int InsertEmbeddedAtSelection(uint flags, nint data, out int start, out int end, out TsTextChange change);

    /// <summary>Calls the native RequestSupportedAttrs operation.</summary>
    /// <param name="flags">The flags argument.</param>
    /// <param name="count">The count argument.</param>
    /// <param name="attributes">The attributes argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int RequestSupportedAttrs(uint flags, uint count, nint attributes);

    /// <summary>Calls the native RequestAttrsAtPosition operation.</summary>
    /// <param name="position">The position argument.</param>
    /// <param name="count">The count argument.</param>
    /// <param name="attributes">The attributes argument.</param>
    /// <param name="flags">The flags argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int RequestAttrsAtPosition(int position, uint count, nint attributes, uint flags);

    /// <summary>Calls the native RequestAttrsTransitioningAtPosition operation.</summary>
    /// <param name="position">The position argument.</param>
    /// <param name="count">The count argument.</param>
    /// <param name="attributes">The attributes argument.</param>
    /// <param name="flags">The flags argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int RequestAttrsTransitioningAtPosition(int position, uint count, nint attributes, uint flags);

    /// <summary>Calls the native FindNextAttrTransition operation.</summary>
    /// <param name="start">The start argument.</param>
    /// <param name="halt">The halt argument.</param>
    /// <param name="count">The count argument.</param>
    /// <param name="attributes">The attributes argument.</param>
    /// <param name="flags">The flags argument.</param>
    /// <param name="next">The next argument.</param>
    /// <param name="found">The found argument.</param>
    /// <param name="offset">The offset argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int FindNextAttrTransition(int start, int halt, uint count, nint attributes, uint flags, out int next, [MarshalAs(UnmanagedType.Bool)] out bool found, out int offset);

    /// <summary>Calls the native RetrieveRequestedAttrs operation.</summary>
    /// <param name="count">The count argument.</param>
    /// <param name="attributes">The attributes argument.</param>
    /// <param name="fetched">The fetched argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int RetrieveRequestedAttrs(uint count, nint attributes, out uint fetched);

    /// <summary>Calls the native GetEndACP operation.</summary>
    /// <param name="end">The end argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int GetEndACP(out int end);

    /// <summary>Calls the native GetActiveView operation.</summary>
    /// <param name="view">The view argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int GetActiveView(out uint view);

    /// <summary>Calls the native GetACPFromPoint operation.</summary>
    /// <param name="view">The view argument.</param>
    /// <param name="point">The point argument.</param>
    /// <param name="flags">The flags argument.</param>
    /// <param name="position">The position argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int GetACPFromPoint(uint view, ref NativeMethods.Point point, uint flags, out int position);

    /// <summary>Calls the native GetTextExt operation.</summary>
    /// <param name="view">The view argument.</param>
    /// <param name="start">The start argument.</param>
    /// <param name="end">The end argument.</param>
    /// <param name="rectangle">The rectangle argument.</param>
    /// <param name="clipped">The clipped argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int GetTextExt(uint view, int start, int end, out NativeMethods.Rect rectangle, [MarshalAs(UnmanagedType.Bool)] out bool clipped);

    /// <summary>Calls the native GetScreenExt operation.</summary>
    /// <param name="view">The view argument.</param>
    /// <param name="rectangle">The rectangle argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int GetScreenExt(uint view, out NativeMethods.Rect rectangle);

    /// <summary>Calls the native GetWnd operation.</summary>
    /// <param name="view">The view argument.</param>
    /// <param name="window">The window argument.</param>
    /// <returns>The native result.</returns>
    [PreserveSig]
    int GetWnd(uint view, out nint window);
}
