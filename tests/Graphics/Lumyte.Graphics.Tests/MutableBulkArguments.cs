using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal partial struct MutableBulkArguments : IShaderArguments
{
    private readonly uint _counter = 42;
    private Storage _data;

    public MutableBulkArguments()
    {
    }

    public uint Counter => _counter;

    public partial uint Number { readonly get; set; }

    public partial Matrix4x4 Transform { readonly get; set; }

    public IGpuRef<uint>? Resource { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Storage
    {
        public uint Number;
        public Matrix4x4 Transform;
        public ShaderReferenceSlot Resource = default;

        public Storage()
        {
        }
    }
}
