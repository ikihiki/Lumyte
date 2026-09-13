namespace Lumyte.Graphics.Portable.Shaders.Offline.Tests;

public sealed class TintReflectionTests
{
    private const string EmptyReflection = """
        {"extensions":[],"entry_points":[{"name":"main","stage":"compute","bindings":[]}],"structures":[]}
        """;

    [Fact]
    public void RejectsMissingInitialIrInsteadOfAssumingNoRoot()
    {
        var error = Assert.Throws<InvalidDataException>(() => TintReflection.Read(EmptyReflection, "", "", [],
            new("unused", "Example", "Example")));

        Assert.Contains("did not return the initial WGSL IR snapshot", error.Message);
    }

    [Fact]
    public void RejectsUnknownImmediateDeclarationsInsteadOfAssumingNoRoot()
    {
        const string ir = """
            == IR dump before wgsl.Lower:
            %root:ptr<immediate, RootData, read> = new_unknown_variable undef
            """;

        var error = Assert.Throws<InvalidDataException>(() => TintReflection.Read(EmptyReflection, "", ir, [],
            new("unused", "Example", "Example")));

        Assert.Contains("without a recognized root variable declaration", error.Message);
    }

    [Fact]
    public void RejectsUnknownWgslExtensionsWithoutInventingRuntimeSupport()
    {
        string reflection = EmptyReflection.Replace("\"extensions\":[]", "\"extensions\":[\"f16\"]", StringComparison.Ordinal);

        var error = Assert.Throws<NotSupportedException>(() => TintReflection.Read(reflection, "",
            "== IR dump before wgsl.Lower:\n%main = @compute func():void {}", [], new("unused", "Example", "Example")));

        Assert.Contains("no feature contract in the Portable runtime", error.Message);
    }

    [Fact]
    public void RejectsAnEntryNameAbsentFromOfficialReflection()
    {
        var error = Assert.Throws<ArgumentException>(() => TintReflection.Read(EmptyReflection, "",
            "== IR dump before wgsl.Lower:\n%main = @compute func():void {}", ["missing"], new("unused", "Example", "Example")));

        Assert.Equal("selectedEntries", error.ParamName);
        Assert.Contains("'missing'", error.Message);
    }
}
