using System.Runtime.InteropServices;

namespace Lumyte.Graphics.Abstractions;

/// <summary>Defines the packed GPU command record for DrawIndexedIndirect.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct DrawIndexedIndirectArguments
{
    /// <summary>Specifies IndexCount.</summary>
    public uint IndexCount;

    /// <summary>Specifies InstanceCount.</summary>
    public uint InstanceCount;

    /// <summary>Specifies FirstIndex.</summary>
    public uint FirstIndex;

    /// <summary>Specifies BaseVertex.</summary>
    public int BaseVertex;

    /// <summary>Specifies FirstInstance. Must be zero for portable indirect draws.</summary>
    public uint FirstInstance;
}
