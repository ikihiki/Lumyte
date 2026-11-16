using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Tests;

internal partial struct OptionalGeneratedClassArguments : IShaderArguments
{
    private NumericData _data;

    public partial uint Value { readonly get; set; }

    private struct NumericData
    {
        public uint Value;
    }
}
