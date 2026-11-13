using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal partial class OptionalGeneratedClassArguments : IShaderArguments
{
    private NumericData _data;

    public partial uint Value { get; set; }

    private struct NumericData
    {
        public uint Value;
    }
}
