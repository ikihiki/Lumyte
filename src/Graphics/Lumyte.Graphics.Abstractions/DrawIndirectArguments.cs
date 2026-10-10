using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Defines the packed GPU command record for DrawIndirect.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct DrawIndirectArguments
{
    /// <summary>Specifies VertexCount.</summary>
    public uint VertexCount;

    /// <summary>Specifies InstanceCount.</summary>
    public uint InstanceCount;

    /// <summary>Specifies FirstVertex.</summary>
    public uint FirstVertex;

    /// <summary>Specifies FirstInstance. Must be zero for portable indirect draws.</summary>
    public uint FirstInstance;
}
