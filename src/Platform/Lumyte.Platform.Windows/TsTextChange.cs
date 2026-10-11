using System.Runtime.InteropServices;

namespace Lumyte.Platform.Windows;

/// <summary>Windows SDK TsTextChange ABI declaration.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TsTextChange
{
    internal int Start;
    internal int OldEnd;
    internal int NewEnd;
}
