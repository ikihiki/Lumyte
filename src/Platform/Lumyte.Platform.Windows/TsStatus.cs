using System.Runtime.InteropServices;

namespace Lumyte.Platform.Windows;

/// <summary>Windows SDK TsStatus ABI declaration.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TsStatus
{
    internal uint Dynamic;
    internal uint Static;
}
