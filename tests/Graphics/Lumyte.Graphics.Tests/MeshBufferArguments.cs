using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal readonly struct MeshBufferArguments(IGpuRef<uint> buffer) : IShaderArguments
{
    public static string ShaderTypeName => "MeshBufferArguments";

    public static int TransferSize => 16;

    public static int ReferenceCount => 1;

    public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Buffer", 0, 16, typeof(ShaderReferenceSlot), true)];

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct => writer.WriteReference(buffer);
}
