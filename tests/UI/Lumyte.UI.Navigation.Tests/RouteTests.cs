using Xunit;

namespace Lumyte.UI.Navigation.Tests;

/// <summary>Checks the in-app route grammar and snapshot ownership boundary.</summary>
public sealed class RouteTests
{
    /// <summary>Rejects URL syntax, noncanonical segments and traversal.</summary>
    /// <param name="value">An invalid route.</param>
    [Theory]
    [InlineData("")]
    [InlineData("settings")]
    [InlineData("/Settings")]
    [InlineData("/settings/")]
    [InlineData("/settings//general")]
    [InlineData("/.")]
    [InlineData("/..")]
    [InlineData("/settings/../general")]
    [InlineData("/settings?mode=1")]
    [InlineData("/settings#general")]
    [InlineData("/documents/{id}")]
    [InlineData("/documents/*")]
    [InlineData("/%61")]
    [InlineData("/日本語")]
    [InlineData("/1settings")]
    public void InvalidRoutesAreRejected(string value) => Assert.Throws<ArgumentException>(() => new UiPageKey<UiNoArgs>(value));

    /// <summary>Accepts only canonical static keys without decoding or normalization.</summary>
    /// <param name="value">A canonical route.</param>
    [Theory]
    [InlineData("/")]
    [InlineData("/settings")]
    [InlineData("/settings/general")]
    [InlineData("/page.v2/data-1_more")]
    public void ValidRoutesRemainOrdinal(string value) => Assert.Equal(value, new UiPageKey<UiNoArgs>(value).Value);

    /// <summary>Checks length bounds, exact generic types and independent copies on capture and read.</summary>
    [Fact]
    public void DestinationsNeverExposeTheirMutableArgumentSnapshot()
    {
        Assert.Throws<ArgumentException>(() => new UiPageKey<int>("/" + new string('a', 256)));
        var key = new UiPageKey<List<int>>("/document");
        var original = new List<int> { 1 };
        var destination = UiPageDestination.Capture(key, original, static value => value.ToList());
        original.Add(2);
        List<int> copy = destination.GetArguments<List<int>>();
        Assert.Equal([1], copy);
        copy.Add(3);
        Assert.Equal([1], destination.GetArguments<List<int>>());
        Assert.Throws<ArgumentException>(() => destination.GetArguments<IReadOnlyList<int>>());
        Assert.Equal("/document", destination.Route);
    }
}
