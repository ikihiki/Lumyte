namespace Lumyte.Graphics.Native.Shaders.Tests.Layouts;

public sealed class NativeShaderInputLayoutTests
{
    [Fact]
    public void LayoutSnapshotsFields()
    {
        NativeShaderInputField expected = new("value", NativeShaderInputFieldKind.Scalar, 0, 4);
        NativeShaderInputField[] fields = [expected];
        NativeShaderInputLayout layout = new("abi-v1", 4, 4, fields);

        fields[0] = expected with { Name = "changed" };

        Assert.Equal(expected, Assert.Single(layout.Fields));
    }

    [Fact]
    public void EmptyRootHasAnExplicitZeroByteLayout()
    {
        NativeShaderInputLayout layout = new("empty-root", 0, 1, []);

        Assert.Equal(0u, layout.Size);
        Assert.Empty(layout.Fields);
    }

    [Theory]
    [InlineData(13u, 4u)]
    [InlineData(uint.MaxValue, 4u)]
    public void FieldMustFitWithinItsDeclaredByteRange(uint offset, uint size)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new NativeShaderInputLayout("abi-v1", 16, 4,
            [new("outside", NativeShaderInputFieldKind.Scalar, offset, size)]));

        Assert.Equal("fields", error.ParamName);
    }

    [Fact]
    public void FieldNamesIdentifyOneLayoutMember()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new NativeShaderInputLayout("abi-v1", 8, 4,
            [new("value", NativeShaderInputFieldKind.Scalar, 0, 4), new("value", NativeShaderInputFieldKind.Scalar, 4, 4)]));

        Assert.Equal("fields", error.ParamName);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(3u)]
    public void LayoutAlignmentHasAPowerOfTwoRepresentation(uint alignment)
    {
        ArgumentOutOfRangeException error = Assert.Throws<ArgumentOutOfRangeException>(() =>
            new NativeShaderInputLayout("abi-v1", 4, alignment, []));

        Assert.Equal("alignment", error.ParamName);
    }
}
