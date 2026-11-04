using Lumyte.Graphics.Primitives.Benchmarks;
using Xunit;

namespace Lumyte.Graphics.Benchmarks.Tests;

/// <summary>Checks recovery CLI isolation and independent analytic pixels without creating native devices.</summary>
public sealed class RecoveryContractTests
{
    /// <summary>Accepts only the dedicated bounded lifecycle mode.</summary>
    [Fact]
    public void ExplicitRecoveryHasBoundedIndependentOptions()
    {
        (string output, int cycles) = RecoveryRunner.Parse(["--output", "fresh-output", "--recovery", "--cycles", "3"]);
        Assert.Equal("fresh-output", output);
        Assert.Equal(3, cycles);
        Assert.Equal(1, RecoveryRunner.Parse(["--recovery"]).Cycles);
    }

    /// <summary>Rejects mixed ordinary flags, unknown options, duplicates and missing or invalid values before GPU creation.</summary>
    /// <param name="arguments">Invalid command line.</param>
    [Theory]
    [InlineData("--recovery --frames 3")]
    [InlineData("--case G06 --recovery")]
    [InlineData("--recovery --warmup 0")]
    [InlineData("--recovery --unknown 1")]
    [InlineData("--recovery --recovery")]
    [InlineData("--recovery --output a --output b")]
    [InlineData("--recovery --cycles 1 --cycles 2")]
    [InlineData("--recovery --cycles")]
    [InlineData("--recovery --output")]
    [InlineData("--recovery --output --cycles 1")]
    [InlineData("--recovery --cycles 0")]
    [InlineData("--recovery --cycles 101")]
    [InlineData("--recovery --cycles -1")]
    [InlineData("--recovery --cycles 1.5")]
    public void InvalidRecoveryArgumentsAreRejected(string arguments)
        => Assert.Throws<ArgumentException>(() => RecoveryRunner.Parse(arguments.Split(' ')));

    /// <summary>Requires explicit fault authorization rather than inferring it from an ordinary G06 run.</summary>
    [Fact]
    public void RecoveryRequiresExplicitMode()
        => Assert.Throws<InvalidOperationException>(() => RecoveryRunner.Parse(["--cycles", "1"]));

    /// <summary>Checks every analytic pixel, channels and exact dimensions rather than relying on a checksum.</summary>
    [Fact]
    public void AnalyticRoundtripRejectsWrongColorMissingAlphaAndTruncatedReadback()
    {
        byte[] red = new byte[32 * 32 * 4];
        byte[] green = new byte[red.Length];
        for (int offset = 0; offset < red.Length; offset += 4)
        {
            red[offset] = 255;
            red[offset + 3] = 255;
            green[offset + 1] = 255;
            green[offset + 3] = 255;
        }

        RecoveryRunner.CheckPixels(red, false);
        RecoveryRunner.CheckPixels(green, true);
        RecoveryRunner.CheckPixels(red, false);
        Assert.Throws<InvalidOperationException>(() => RecoveryRunner.CheckPixels(red, true));
        Assert.Throws<InvalidOperationException>(() => RecoveryRunner.CheckPixels(green, false));
        Assert.Throws<InvalidOperationException>(() => RecoveryRunner.CheckPixels(red[..^4], false));
        red[^1] = 0;
        Assert.Throws<InvalidOperationException>(() => RecoveryRunner.CheckPixels(red, false));
        red[^1] = 255;
        red[^2] = 2;
        Assert.Throws<InvalidOperationException>(() => RecoveryRunner.CheckPixels(red, false));
    }
}
