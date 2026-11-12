using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal readonly record struct NumericBitsArguments(IGpuRef<Vector4> Data, IGpuRef<uint> Output) : IShaderArguments
{
    static string IShaderArguments.ShaderTypeName => nameof(NumericBitsArguments);

    void IShaderArguments.Write<TWriter>(ref TWriter writer)
    {
        writer.WriteReference(nameof(Data), Data);
        writer.WriteReference(nameof(Output), Output);
    }
}
