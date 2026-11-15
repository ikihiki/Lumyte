using System.Collections;
using System.Collections.Frozen;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Checks the complete CSS Color 4 palette against an independent standards fixture.</summary>
public sealed class CssColorsTests
{
    // Transcribed from the named-color-table, not from CssColors or its All dictionary:
    // https://github.com/w3c/csswg-drafts/blob/8e3693ea9baa53f5411eb18d3b18201e8c15fb14/css-color-4/Overview.bs#L1633
    // All 148 RGB triples cross-checked against the maintained color-name data:
    // https://github.com/colorjs/color-name/blob/9ec3195524951f58bc1d33797fccc0f8ecfe070c/index.js
    // transparent is separately defined as transparent black by CSS Color 4.
    private const string NamedColorCsv = """
        aliceblue,f0f8ff
        antiquewhite,faebd7
        aqua,00ffff
        aquamarine,7fffd4
        azure,f0ffff
        beige,f5f5dc
        bisque,ffe4c4
        black,000000
        blanchedalmond,ffebcd
        blue,0000ff
        blueviolet,8a2be2
        brown,a52a2a
        burlywood,deb887
        cadetblue,5f9ea0
        chartreuse,7fff00
        chocolate,d2691e
        coral,ff7f50
        cornflowerblue,6495ed
        cornsilk,fff8dc
        crimson,dc143c
        cyan,00ffff
        darkblue,00008b
        darkcyan,008b8b
        darkgoldenrod,b8860b
        darkgray,a9a9a9
        darkgreen,006400
        darkgrey,a9a9a9
        darkkhaki,bdb76b
        darkmagenta,8b008b
        darkolivegreen,556b2f
        darkorange,ff8c00
        darkorchid,9932cc
        darkred,8b0000
        darksalmon,e9967a
        darkseagreen,8fbc8f
        darkslateblue,483d8b
        darkslategray,2f4f4f
        darkslategrey,2f4f4f
        darkturquoise,00ced1
        darkviolet,9400d3
        deeppink,ff1493
        deepskyblue,00bfff
        dimgray,696969
        dimgrey,696969
        dodgerblue,1e90ff
        firebrick,b22222
        floralwhite,fffaf0
        forestgreen,228b22
        fuchsia,ff00ff
        gainsboro,dcdcdc
        ghostwhite,f8f8ff
        gold,ffd700
        goldenrod,daa520
        gray,808080
        green,008000
        greenyellow,adff2f
        grey,808080
        honeydew,f0fff0
        hotpink,ff69b4
        indianred,cd5c5c
        indigo,4b0082
        ivory,fffff0
        khaki,f0e68c
        lavender,e6e6fa
        lavenderblush,fff0f5
        lawngreen,7cfc00
        lemonchiffon,fffacd
        lightblue,add8e6
        lightcoral,f08080
        lightcyan,e0ffff
        lightgoldenrodyellow,fafad2
        lightgray,d3d3d3
        lightgreen,90ee90
        lightgrey,d3d3d3
        lightpink,ffb6c1
        lightsalmon,ffa07a
        lightseagreen,20b2aa
        lightskyblue,87cefa
        lightslategray,778899
        lightslategrey,778899
        lightsteelblue,b0c4de
        lightyellow,ffffe0
        lime,00ff00
        limegreen,32cd32
        linen,faf0e6
        magenta,ff00ff
        maroon,800000
        mediumaquamarine,66cdaa
        mediumblue,0000cd
        mediumorchid,ba55d3
        mediumpurple,9370db
        mediumseagreen,3cb371
        mediumslateblue,7b68ee
        mediumspringgreen,00fa9a
        mediumturquoise,48d1cc
        mediumvioletred,c71585
        midnightblue,191970
        mintcream,f5fffa
        mistyrose,ffe4e1
        moccasin,ffe4b5
        navajowhite,ffdead
        navy,000080
        oldlace,fdf5e6
        olive,808000
        olivedrab,6b8e23
        orange,ffa500
        orangered,ff4500
        orchid,da70d6
        palegoldenrod,eee8aa
        palegreen,98fb98
        paleturquoise,afeeee
        palevioletred,db7093
        papayawhip,ffefd5
        peachpuff,ffdab9
        peru,cd853f
        pink,ffc0cb
        plum,dda0dd
        powderblue,b0e0e6
        purple,800080
        rebeccapurple,663399
        red,ff0000
        rosybrown,bc8f8f
        royalblue,4169e1
        saddlebrown,8b4513
        salmon,fa8072
        sandybrown,f4a460
        seagreen,2e8b57
        seashell,fff5ee
        sienna,a0522d
        silver,c0c0c0
        skyblue,87ceeb
        slateblue,6a5acd
        slategray,708090
        slategrey,708090
        snow,fffafa
        springgreen,00ff7f
        steelblue,4682b4
        tan,d2b48c
        teal,008080
        thistle,d8bfd8
        tomato,ff6347
        turquoise,40e0d0
        violet,ee82ee
        wheat,f5deb3
        white,ffffff
        whitesmoke,f5f5f5
        yellow,ffff00
        yellowgreen,9acd32
        """;

    /// <summary>Every keyword has the exact standard RGB code and opaque alpha.</summary>
    [Fact]
    public void CompletePaletteMatchesIndependentStandardFixture()
    {
        Dictionary<string, SRgba<byte>> expected = CreateExpected();
        Assert.Equal(149, expected.Count);
        Assert.Equal(149, CssColors.All.Count);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), CssColors.All.Keys.Order(StringComparer.Ordinal));
        foreach ((string keyword, SRgba<byte> value) in expected)
        {
            Assert.Equal(keyword.ToLowerInvariant(), keyword);
            Assert.Equal(value, CssColors.All[keyword].ToCodes());
            Assert.True(CssColors.TryGet(keyword, out SRgba8 actual), keyword);
            Assert.Equal(value, actual.ToCodes());
            Assert.Equal(keyword == "transparent" ? (byte)0 : (byte)255, actual.A);
        }
    }

    /// <summary>The catalog exposes only All and TryGet, with no former global named fields.</summary>
    [Fact]
    public void PublicSurfaceIsOnlyTheAgreedCatalog()
    {
        Type type = typeof(CssColors);
        Assert.True(type.IsAbstract && type.IsSealed);
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
        FieldInfo[] fields = type.GetFields(Flags);
        Assert.Empty(fields);

        PropertyInfo property = Assert.Single(type.GetProperties(Flags));
        Assert.Equal("All", property.Name);
        Assert.Equal(typeof(IReadOnlyDictionary<string, SRgba8>), property.PropertyType);
        Assert.Null(property.SetMethod);
        Assert.Equal(new[] { "TryGet", "get_All" }, type.GetMethods(Flags).Select(method => method.Name).Order(StringComparer.Ordinal));
        Assert.Empty(type.GetConstructors(Flags));
        Assert.Empty(type.GetEvents(Flags));
        Assert.Empty(type.GetNestedTypes(BindingFlags.Public));
    }

    /// <summary>Aliases retain separate names, and frequently confused CSS values stay distinct.</summary>
    [Fact]
    public void AliasesAndCharacteristicColorsHaveCssSemantics()
    {
        string[] grayNames = ["gray", "darkgray", "dimgray", "lightgray", "slategray", "darkslategray", "lightslategray"];
        foreach (string gray in grayNames)
        {
            Assert.Equal(CssColors.All[gray], CssColors.All[gray.Replace("gray", "grey", StringComparison.Ordinal)]);
        }

        Assert.Equal(SRgba8.Aqua, SRgba8.Cyan);
        Assert.Equal(SRgba8.Fuchsia, SRgba8.Magenta);
        Assert.Equal(new SRgba<byte>(0, 128, 0, 255), SRgba8.Green.ToCodes());
        Assert.Equal(new SRgba<byte>(0, 255, 0, 255), SRgba8.Lime.ToCodes());
        Assert.NotEqual(SRgba8.Green, SRgba8.Lime);
        Assert.Equal(new SRgba<byte>(102, 51, 153, 255), SRgba8.RebeccaPurple.ToCodes());
        Assert.Equal(new SRgba<byte>(100, 149, 237, 255), SRgba8.CornflowerBlue.ToCodes());
        Assert.Equal(new SRgba<byte>(0, 0, 0, 0), SRgba8.Transparent.ToCodes());
        Assert.NotEqual(SRgba8.White.WithAlpha(0), SRgba8.Transparent);
    }

    /// <summary>Lookup is ordinal case-insensitive, including in a Turkish culture.</summary>
    [Fact]
    public void LookupIgnoresOrdinalCaseRatherThanCurrentCulture()
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            foreach ((string name, SRgba<byte> value) in CreateExpected())
            {
                string upper = name.ToUpperInvariant();
                string mixed = string.Concat(name.Select((letter, index) => index % 2 == 0 ? char.ToUpperInvariant(letter) : letter));
                Assert.True(CssColors.TryGet(upper, out SRgba8 actual));
                Assert.Equal(value, actual.ToCodes());
                Assert.True(CssColors.TryGet(mixed, out actual));
                Assert.Equal(value, actual.ToCodes());
                Assert.Equal(value, CssColors.All[upper].ToCodes());
                Assert.Equal(value, CssColors.All[mixed].ToCodes());
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    /// <summary>Lookup does not become a CSS parser, whitespace normalizer, or theme resolver.</summary>
    [Fact]
    public void InvalidNamesReturnFalseAndDefault()
    {
        string?[] invalid = [null, string.Empty, " ", " red", "red ", "\tred\n", "red\0", "unknown", "#ff0000", "ff0000", "rgb(255, 0, 0)", "rgba(0,0,0,0)", "hsl(0 100% 50%)", "currentColor", "Canvas", "CanvasText", "ButtonFace", "AccentColor", "inherit", "initial", "revert", "none", "lİme"];
        foreach (string? name in invalid)
        {
            SRgba8 result = SRgba8.Red;
            Assert.False(CssColors.TryGet(name, out result));
            Assert.Equal(default, result);
            if (name is not null)
            {
                Assert.False(CssColors.All.ContainsKey(name));
            }
        }
    }

    /// <summary>Raw casts preserve 255 codes; explicit normalization supplies fractional values.</summary>
    [Fact]
    public void CodesRequireExplicitNormalizationForEveryNumericStorage()
    {
        Assert.Equal(new SRgba<float>(100, 149, 237, 255), SRgba8.CornflowerBlue.ToCodes().ConvertChecked<float>());
        Assert.Equal(new SRgba<float>(1, 0, 0, 1), SRgba8.Red.ToCodes().NormalizeUnorm<float>(255));
        CheckNormalization<byte>();
        CheckNormalization<short>();
        CheckNormalization<ushort>();
        CheckNormalization<int>();
        CheckNormalization<uint>();
        CheckNormalization<long>();
        CheckNormalization<ulong>();
        CheckNormalization<nint>();
        CheckNormalization<nuint>();
        CheckNormalization<Int128>();
        CheckNormalization<UInt128>();
        CheckNormalization<BigInteger>();
        CheckNormalization<decimal>();
        CheckNormalization<Half>();
        CheckNormalization<float>();
        CheckNormalization<double>();
        Assert.Throws<OverflowException>(() => SRgba8.CornflowerBlue.ToCodes().ConvertChecked<sbyte>());
    }

    /// <summary>The frozen map rejects all mutation paths and gives callers value copies.</summary>
    [Fact]
    public void AllIsFrozenAndCannotBeMutated()
    {
        IReadOnlyDictionary<string, SRgba8> all = CssColors.All;
        Assert.Same(all, CssColors.All);
        FrozenDictionary<string, SRgba8> frozen = Assert.IsAssignableFrom<FrozenDictionary<string, SRgba8>>(all);
        Assert.Equal(StringComparer.OrdinalIgnoreCase, frozen.Comparer);
        if (all is IDictionary<string, SRgba8> generic)
        {
            Assert.True(generic.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => generic["red"] = default);
            Assert.Throws<NotSupportedException>(() => generic.Add("notacolor", default));
            Assert.Throws<NotSupportedException>(() => generic.Remove("red"));
            Assert.Throws<NotSupportedException>(() => generic.Clear());
        }

        if (all is IDictionary dictionary)
        {
            Assert.True(dictionary.IsReadOnly);
            Assert.Throws<NotSupportedException>(() => dictionary["red"] = default(SRgba8));
            Assert.Throws<NotSupportedException>(() => dictionary.Clear());
        }

        SRgba8 copy = all["red"].WithAlpha(0);
        Assert.Equal((byte)0, copy.A);
        Assert.Equal(new SRgba<byte>(255, 0, 0, 255), all["red"].ToCodes());
        Assert.Equal(149, all.Count);
        Assert.False(all.ContainsKey("notacolor"));
    }

    /// <summary>Concurrent reads never observe incomplete catalog publication or mutated entries.</summary>
    [Fact]
    public void ConcurrentLookupMatchesIndependentFixture()
    {
        KeyValuePair<string, SRgba<byte>>[] expected = CreateExpected().ToArray();
        Parallel.For(0, 32, iteration =>
        {
            foreach ((string name, SRgba<byte> value) in expected)
            {
                string key = iteration % 2 == 0 ? name : name.ToUpperInvariant();
                Assert.True(CssColors.TryGet(key, out SRgba8 actual));
                Assert.Equal(value, actual.ToCodes());
                Assert.Equal(value, CssColors.All[key].ToCodes());
            }

            Assert.False(CssColors.TryGet("currentColor", out SRgba8 missing));
            Assert.Equal(default, missing);
        });
    }

    internal static Dictionary<string, SRgba<byte>> CreateExpected()
    {
        var expected = new Dictionary<string, SRgba<byte>>(StringComparer.Ordinal);
        foreach (string line in NamedColorCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] pair = line.Split(',');
            Assert.Equal(2, pair.Length);
            byte[] rgb = Convert.FromHexString(pair[1]);
            Assert.Equal(3, rgb.Length);
            expected.Add(pair[0], new SRgba<byte>(rgb[0], rgb[1], rgb[2], 255));
        }

        Assert.Equal(148, expected.Count);
        expected.Add("transparent", new SRgba<byte>(0, 0, 0, 0));
        return expected;
    }

    private static void CheckNormalization<T>()
        where T : INumber<T>
    {
        SRgba<T> raw = SRgba8.CornflowerBlue.ToCodes().ConvertChecked<T>();
        Assert.Equal(T.CreateChecked(255), raw.A);
        Assert.Equal(new SRgba<T>(T.CreateChecked(100), T.CreateChecked(149), T.CreateChecked(237), T.CreateChecked(255)), raw);
        Assert.Equal(new SRgba<double>(100d / 255, 149d / 255, 237d / 255, 1), raw.NormalizeUnorm<double>(T.CreateChecked(255)));
        Assert.Equal(new SRgba<decimal>(100m / 255, 149m / 255, 237m / 255, 1), raw.NormalizeUnorm<decimal>(T.CreateChecked(255)));
        Assert.Throws<ArithmeticException>(() => raw.NormalizeUnorm<int>(T.CreateChecked(255)));
        Assert.Throws<ArithmeticException>(() => raw.NormalizeUnorm<BigInteger>(T.CreateChecked(255)));
        Assert.Equal(new SRgba<BigInteger>(1, 0, 0, 1), SRgba8.Red.ToCodes().ConvertChecked<T>().NormalizeUnorm<BigInteger>(T.CreateChecked(255)));
    }
}
