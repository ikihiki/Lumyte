using System.Runtime.InteropServices;

namespace Lumyte.Platform.Windows;

/// <summary>Windows SDK TsSelection ABI declaration.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TsSelection
{
    internal int Start;
    internal int End;
    internal uint ActiveEnd;
    internal int Interim;
}
