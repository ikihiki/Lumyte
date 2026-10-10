using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Defines the packed GPU command record for DispatchIndirect.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct DispatchIndirectArguments
{
    /// <summary>Specifies GroupCountX.</summary>
    public uint GroupCountX;

    /// <summary>Specifies GroupCountY.</summary>
    public uint GroupCountY;

    /// <summary>Specifies GroupCountZ.</summary>
    public uint GroupCountZ;
}
