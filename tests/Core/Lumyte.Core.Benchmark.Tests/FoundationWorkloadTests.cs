using Lumyte.Mathematics.Benchmark.GeometryQueries;
using Lumyte.Mathematics.Geometry;
using Lumyte.Settings.Benchmark.FileSettingsStorage;
using Lumyte.Settings.Benchmark.PersistedSettings;
using Lumyte.TextEditing;
using Lumyte.TextEditing.Benchmark.TextDocuments;
using Xunit;

namespace Lumyte.Core.Benchmark.Tests;

/// <summary>Functional acceptance of independently selectable foundation workloads.</summary>
public class FoundationWorkloadTests
{
    /// <summary>Checks spatial queries against analytic, nontrivial expected results.</summary>
    [Fact]
    public void SpatialOracles()
    {
        var benchmark = new SpatialBenchmarks();
        var shapes = new ShapeBenchmarks();
        Assert.Equal(3, shapes.Circle());
        Assert.Equal(3, shapes.Ellipse());
        Assert.Equal(3, shapes.RoundedRectangle());
        Assert.Equal(7, benchmark.Distance());
        Assert.Equal(new Point3D<double>(2, 0, 0), benchmark.Project());
        Assert.Equal(new AxisAlignedBox3D<double>(new(-2, -5, -3), new(4, 3, 7)), benchmark.Bounds());
        Assert.Equal(3, benchmark.SphereQueries());
        Assert.Equal(new AxisAlignedBox3D<double>(new(1, 2, 3), new(4, 5, 6)), benchmark.Intersect());
    }

    /// <summary>Checks actual bytes before and after two distinct atomic replacements.</summary>
    /// <param name="bytes">The payload size.</param>
    /// <returns>The asynchronous acceptance result.</returns>
    [Theory]
    [InlineData(64)]
    [InlineData(4096)]
    [InlineData(65536)]
    public async Task DirectStoreOraclesAsync(int bytes)
    {
        var benchmark = new JsonFileSettingsStoreBenchmarks { Bytes = bytes };
        benchmark.Setup();
        try
        {
            byte[] expected = Enumerable.Range(0, bytes).Select(index => (byte)(index % 251)).ToArray();
            Assert.Equal(expected, await benchmark.Read());
            Assert.Null(await benchmark.Missing());
            await benchmark.Write();
            byte[]? changed = await benchmark.Read();
            Assert.Equal(expected.Select(value => (byte)(value ^ 0x5a)).ToArray(), changed);
            await benchmark.Write();
            Assert.Equal(expected, await benchmark.Read());
            Assert.Null(await benchmark.Missing());
        }
        finally
        {
            benchmark.Cleanup();
        }
    }

    /// <summary>Checks actual source validation and nested provider publication without DI.</summary>
    /// <param name="profile">The public source input profile.</param>
    /// <param name="items">The section item count.</param>
    /// <returns>The asynchronous source assertion.</returns>
    [Theory]
    [InlineData("Valid", 1)]
    [InlineData("Valid", 16)]
    [InlineData("Valid", 256)]
    [InlineData("Duplicate", 16)]
    [InlineData("Unsupported", 16)]
    [InlineData("Missing", 16)]
    public async Task SettingsSourceOraclesAsync(string profile, int items)
    {
        var benchmark = new PersistedSettingsSourceBenchmarks { Profile = profile, Items = items };
        benchmark.Setup();
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(profile == "Valid" ? (items - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : null, benchmark.FlattenSections());
            Lumyte.Settings.PersistedSettingsSource source = await benchmark.Load();
            Microsoft.Extensions.Configuration.IConfigurationProvider provider = source.Build(new Microsoft.Extensions.Configuration.ConfigurationBuilder());
            provider.Load();
            Assert.True(provider.TryGet("documentVersion", out string? version));
            Assert.Equal("1", version);
            Assert.Equal(profile == "Valid", provider.TryGet("sections:sample:values:number", out string? number));
            Assert.Equal(profile == "Valid" ? "17" : null, number);
        }
    }

    /// <summary>Distinguishes cancellation restoration from committing changed IME text.</summary>
    /// <param name="length">The document code-unit length.</param>
    [Theory]
    [InlineData(256)]
    [InlineData(4096)]
    [InlineData(32768)]
    public void CompositionCheckpointOracles(int length)
    {
        var benchmark = new TextBenchmarks { Length = length };
        benchmark.Setup();
        string original = string.Concat(Enumerable.Repeat("a日e\u0301", length / 4));
        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(original, benchmark.CompositionLifetime());
            TextDocumentSnapshot committed = benchmark.CompositionCommit();
            Assert.Equal("候" + original[1..], committed.Text);
            Assert.Equal(committed.Text, committed.CommittedText);
            Assert.Null(committed.Composition);
            Assert.Equal(new TextSelection(1, 1), committed.Selection);
            Assert.True(committed.Revision >= 2);
        }
    }
}
