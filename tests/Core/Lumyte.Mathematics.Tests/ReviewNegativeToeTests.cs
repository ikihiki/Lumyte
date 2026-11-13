using System.Numerics;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Regressions for review 4558173316: exact negative toe results must not round into valid black.</summary>
public sealed class ReviewNegativeToeTests
{
    /// <summary>Every RGB channel rejects negative exact results in either extrapolation direction.</summary>
    /// <param name="precision">The IEEE component precision.</param>
    [Theory]
    [InlineData("half")]
    [InlineData("float")]
    [InlineData("double")]
    public void NegativeExactToeIsRejected(string precision)
    {
        switch (precision)
        {
            case "half": CheckNegative<Half, Half>(); break;
            case "float": CheckNegative<float, float>(); break;
            case "double": CheckNegative<double, double>(); break;
            default: throw new ArgumentOutOfRangeException(nameof(precision));
        }
    }

    /// <summary>Narrower result precisions cannot hide an exact negative source value.</summary>
    [Fact]
    public void NarrowerResultPrecisionStillRejectsNegativeExactToe()
    {
        CheckNegative<double, float>();
        CheckNegative<float, Half>();
    }

    /// <summary>Nonnegative exact values may still round to zero, and valid extrapolation remains supported.</summary>
    /// <param name="precision">The IEEE component precision.</param>
    [Theory]
    [InlineData("half")]
    [InlineData("float")]
    [InlineData("double")]
    public void NonnegativeToeBehaviorIsPreserved(string precision)
    {
        switch (precision)
        {
            case "half": CheckNonnegative<Half>(); break;
            case "float": CheckNonnegative<float>(); break;
            case "double": CheckNonnegative<double>(); break;
            default: throw new ArgumentOutOfRangeException(nameof(precision));
        }
    }

    /// <summary>General decoding must not hide an exact negative linear result through result underflow.</summary>
    /// <param name="precision">The IEEE result precision.</param>
    [Theory]
    [InlineData("half")]
    [InlineData("float")]
    [InlineData("double")]
    public void NonToeNegativeResultIsRejected(string precision)
    {
        switch (precision)
        {
            case "half": CheckNonToe<Half>(); break;
            case "float": CheckNonToe<float>(); break;
            case "double": CheckNonToe<double>(); break;
            default: throw new ArgumentOutOfRangeException(nameof(precision));
        }
    }

    private static void CheckNonToe<T>()
        where T : IFloatingPointIeee754<T>
    {
        var first = new SRgba<T>(T.Zero, T.Zero, T.Zero, T.One);
        for (int channel = 0; channel < 3; channel++)
        {
            T value = T.CreateChecked(0.5);
            var last = new SRgba<T>(channel == 0 ? value : T.Zero, channel == 1 ? value : T.Zero, channel == 2 ? value : T.Zero, T.One);
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => first.InterpolateSrgbLinearPremultiplied(last, -T.BitIncrement(T.Zero)));
            Assert.Equal("amount", exception.ParamName);
        }
    }

    private static void CheckNegative<TSource, TResult>()
        where TSource : IFloatingPointIeee754<TSource>
        where TResult : IFloatingPointIeee754<TResult>
    {
        TSource epsilon = TSource.BitIncrement(TSource.Zero);
        var black = new SRgba<TSource>(TSource.Zero, TSource.Zero, TSource.Zero, TSource.One);
        for (int channel = 0; channel < 3; channel++)
        {
            var endpoint = new SRgba<TSource>(channel == 0 ? epsilon : TSource.Zero, channel == 1 ? epsilon : TSource.Zero, channel == 2 ? epsilon : TSource.Zero, TSource.One);
            AssertRejected(black, endpoint, -TResult.One);
            AssertRejected(black, endpoint, -TResult.CreateChecked(0.25));
            AssertRejected(endpoint, black, TResult.CreateChecked(2));
            AssertRejected(black.WithAlpha(TSource.CreateChecked(0.25)), endpoint.WithAlpha(TSource.CreateChecked(0.75)), -TResult.CreateChecked(0.25));
        }

        static void AssertRejected(SRgba<TSource> first, SRgba<TSource> last, TResult amount)
        {
            ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => first.InterpolateSrgbLinearPremultiplied(last, amount));
            Assert.Equal("amount", exception.ParamName);
        }
    }

    private static void CheckNonnegative<T>()
        where T : IFloatingPointIeee754<T>
    {
        T epsilon = T.BitIncrement(T.Zero);
        var black = new SRgba<T>(T.Zero, T.Zero, T.Zero, T.One);
        var endpoint = new SRgba<T>(epsilon, T.Zero, T.Zero, T.One);
        Assert.Equal(T.Zero, black.InterpolateSrgbLinearPremultiplied(endpoint, T.CreateChecked(0.25)).R);
        var twice = new SRgba<T>(T.CreateChecked(2) * epsilon, T.Zero, T.Zero, T.One);
        Assert.Equal(T.Zero, endpoint.InterpolateSrgbLinearPremultiplied(twice, -T.One).R);
        var first = new SRgba<T>(T.CreateChecked(6) * epsilon, T.Zero, T.Zero, T.One);
        var last = new SRgba<T>(T.CreateChecked(7) * epsilon, T.Zero, T.Zero, T.One);
        Assert.Equal(T.CreateChecked(5) * epsilon, first.InterpolateSrgbLinearPremultiplied(last, -T.One).R);
        Assert.Equal(first, first.InterpolateSrgbLinearPremultiplied(last, T.Zero));
        Assert.Equal(last, first.InterpolateSrgbLinearPremultiplied(last, T.One));
    }
}
