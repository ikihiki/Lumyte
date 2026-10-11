using System.Runtime.InteropServices;

namespace Lumyte.Platform.Windows;

/// <summary>Windows SDK TsRunInfo ABI declaration.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TsRunInfo
{
    internal uint Count;
    internal uint Type;
}
