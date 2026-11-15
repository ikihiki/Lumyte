using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal readonly struct TypedSamplerValue(IGpuRef<IGraphicsSampler>? value) : IShaderArguments
{
    public static string ShaderTypeName => "TypedSamplerValue";

    public static int TransferSize => 16;

    public static int ReferenceCount => 1;

    public static IReadOnlyList<ShaderTransferField> TransferFields => [new("Value", 0, 16, typeof(IGraphicsSampler), true)];

    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct => writer.WriteReference(value);
}
