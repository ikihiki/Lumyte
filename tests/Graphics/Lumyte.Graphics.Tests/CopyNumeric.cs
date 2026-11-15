using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal readonly partial struct CopyNumeric(uint value) : IShaderArguments
{
    private readonly Storage _data = new(value);

    public partial uint Value { get; }

    private readonly struct Storage(uint value)
    {
        public readonly uint Value = value;
    }
}
