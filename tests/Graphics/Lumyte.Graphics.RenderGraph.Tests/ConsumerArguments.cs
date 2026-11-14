using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

// Explicit application wire layout shared by direct encoders and external graph consumers.
internal record struct ConsumerArguments(float Float, Func<float>? Read = null) : IShaderArguments
{
    public static string ShaderTypeName => "ConsumerInputs";

    public static int TransferSize => 128;

    public static int ReferenceCount => 0;

    public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Float", 0, 4, typeof(float)), new("Int", 4, 4, typeof(int)), new("UInt", 8, 4, typeof(uint)), new("Vector2", 16, 8, typeof(Vector2)), new("Vector3", 32, 12, typeof(Vector3)), new("Vector4", 48, 16, typeof(Vector4)), new("Matrix", 64, 64, typeof(Matrix4x4))];

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        var wire = new Wire { Float = Read?.Invoke() ?? Float, Int = -2, UInt = 3, Vector2 = Vector2.One, Vector3 = Vector3.One, Vector4 = Vector4.One, Matrix = Matrix4x4.Identity };
        writer.WriteValue(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in wire, 1)));
    }

    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct Wire
    {
        [FieldOffset(0)]
        public float Float;

        [FieldOffset(4)]
        public int Int;

        [FieldOffset(8)]
        public uint UInt;

        [FieldOffset(16)]
        public Vector2 Vector2;

        [FieldOffset(32)]
        public Vector3 Vector3;

        [FieldOffset(48)]
        public Vector4 Vector4;

        [FieldOffset(64)]
        public Matrix4x4 Matrix;
    }
}
