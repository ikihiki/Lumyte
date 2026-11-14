using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed record class ConsumerTextureArguments(IGpuRef<IGraphicsTextureView> Input, IGpuRef<IGraphicsTextureView> Backdrop, float Float) : IShaderArguments
{
    public static string ShaderTypeName => "ConsumerTextureInputs";

    public static int TransferSize => 48;

    public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Input", 0, 16, typeof(ShaderReferenceSlot), true), new("Backdrop", 16, 16, typeof(ShaderReferenceSlot), true), new("Float", 32, 4, typeof(float))];

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct
    {
        var wire = new Wire { Float = Float };
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in wire, 1));
        writer.WriteReference(Input);
        writer.WriteReference(Backdrop);
        writer.WriteValue(bytes[32..]);
    }

    [StructLayout(LayoutKind.Explicit, Size = 48)]
    private struct Wire
    {
        [FieldOffset(0)]
        public ShaderReferenceSlot Input;

        [FieldOffset(16)]
        public ShaderReferenceSlot Backdrop;

        [FieldOffset(32)]
        public float Float;
    }
}
