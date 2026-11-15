using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Reserves the common 16-byte GPU reference ABI in private unmanaged wire storage.</summary>
[StructLayout(LayoutKind.Sequential, Size = 16, Pack = 4)]
public struct ShaderReferenceSlot
{
}
