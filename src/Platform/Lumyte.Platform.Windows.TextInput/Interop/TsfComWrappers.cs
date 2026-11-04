using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Windows.Win32;

namespace Lumyte.Platform.Windows.TextInput.Interop;

internal sealed unsafe class TsfComWrappers : ComWrappers
{
    private static readonly ComInterfaceEntry* _store = CreateEntries(Entry<ITextStoreACP>(), Entry<ITfContextOwnerCompositionSink>(), Entry<ITfTextEditSink>());
    private static readonly ComInterfaceEntry* _candidates = CreateEntries(Entry<ITfUIElementSink>());
    private static readonly ComInterfaceEntry* _sink = CreateEntries(Entry<ITextStoreACPSink>());

    protected override ComInterfaceEntry* ComputeVtables(object obj, CreateComInterfaceFlags flags, out int count)
    {
        if (obj is ITextStoreACP.Interface and ITfContextOwnerCompositionSink.Interface and ITfTextEditSink.Interface)
        {
            count = 3;
            return _store;
        }

        count = 1;
        return obj switch
        {
            ITfUIElementSink.Interface => _candidates,
            ITextStoreACPSink.Interface => _sink,
            _ => throw new NotSupportedException("Unsupported TSF callback object."),
        };
    }

    protected override object CreateObject(nint externalComObject, CreateObjectFlags flags) => throw new NotSupportedException("Native TSF objects use CsWin32 pointers.");

    protected override void ReleaseObjects(System.Collections.IEnumerable objects) => throw new NotSupportedException("Native TSF objects use explicit reference ownership.");

    private static ComInterfaceEntry Entry<T>()
        where T : unmanaged, IComIID, IVTable => new() { IID = T.Guid, Vtable = (nint)T.VTable };

    private static ComInterfaceEntry* CreateEntries(params ComInterfaceEntry[] entries)
    {
        var memory = (ComInterfaceEntry*)RuntimeHelpers.AllocateTypeAssociatedMemory(typeof(TsfComWrappers), sizeof(ComInterfaceEntry) * entries.Length);
        entries.AsSpan().CopyTo(new Span<ComInterfaceEntry>(memory, entries.Length));
        return memory;
    }
}
