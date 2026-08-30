using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Lumyte.Resources.Tests;

public sealed class AssetKeyTests
{
    [Fact]
    public void StoresOneReferenceAndTwoPositions() => Assert.Equal(IntPtr.Size + (sizeof(int) * 2), Unsafe.SizeOf<AssetKey<TestResource>>());

    [Fact]
    public void ExposesCanonicalComponentsWithoutParsingAgain()
    {
        AssetKey<TestResource> key = Asset.From<TestResource>(
            "HTTPS://cdn.example.com/models/robot.glb",
            new NamedSelector("Body"));

        Assert.Equal("https", key.Scheme);
        Assert.Equal("//cdn.example.com/models/robot.glb", key.Address);
        Assert.Equal("part/name/Body", key.Selector);
    }

    [Fact]
    public void NormalizesFilePathAndEscapesSelector()
    {
        AssetKey<TestResource> key = Asset.File<TestResource>(
            "models\\characters\\..\\robot.gltf",
            new NamedSelector("Body/Main"));

        Assert.Equal(
            "AssetKey<TestResource>(file:models/robot.gltf#part/name/Body%2FMain)",
            key.ToString());
    }

    [Fact]
    public void SupportsCustomSchemes()
    {
        AssetKey<TestResource> key = Asset.From<TestResource>(
            "HTTPS://cdn.example.com/models/robot.glb",
            new NamedSelector("Body"));

        Assert.Equal(
            "AssetKey<TestResource>(https://cdn.example.com/models/robot.glb#part/name/Body)",
            key.ToString());
    }

    [Fact]
    public void AllowsUnrestrictedNonemptySchemes()
    {
        var key = AssetKey<TestResource>.Parse(
            "1 CUSTOM:resource");

        Assert.Equal("1 custom", key.Scheme);
        Assert.Equal("resource", key.Address);
    }

    [Fact]
    public void LeavesAddressTextValidationToResolversAndLoaders()
    {
        var key = AssetKey<TestResource>.Parse(
            "custom:raw path\\with%invalid");

        Assert.Equal("raw path\\with%invalid", key.Address);
    }

    [Fact]
    public void LeavesSelectorValidationToLoaders()
    {
        var key = AssetKey<TestResource>.Parse(
            "custom:resource#//part#detail/");

        Assert.Equal("//part#detail/", key.Selector);
    }

    [Fact]
    public void RoundTripsAsJsonString()
    {
        AssetKey<TestResource> expected = Asset.Id<TestResource>(
            "character.robot",
            new NamedSelector("Body"));
        JsonSerializerOptions options = new();
        options.Converters.Add(new AssetKeyJsonConverter<TestResource>());

        string json = JsonSerializer.Serialize(expected, options);
        AssetKey<TestResource> actual =
            JsonSerializer.Deserialize<AssetKey<TestResource>>(json, options);

        Assert.Equal("\"asset:character.robot#part/name/Body\"", json);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RejectsFilePathsOutsideSourceRoot()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => Asset.File<TestResource>("../secret.bin"));

        Assert.Equal("path", exception.ParamName);
    }

    private sealed class TestResource;

    private readonly record struct NamedSelector(string Name)
        : IResourceSelector<TestResource>
    {
        public void WriteTo(ResourceSelectorBuilder builder)
        {
            builder.WriteSegment("part");
            builder.WriteSegment("name");
            builder.WriteSegment(Name);
        }
    }
}
