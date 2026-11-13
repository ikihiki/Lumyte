using System.Numerics;
using System.Reflection;
using Lumyte.Mathematics.Colors;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Independent adversarial checks of the typed consumer contract.</summary>
public sealed class TypedColorApiReviewTests
{
    private static readonly Type[] _layouts =
    [
        typeof(SGray<>), typeof(SGrayAlpha<>), typeof(SRgb<>), typeof(SRgba<>),
        typeof(SBgr<>), typeof(SBgra<>), typeof(SArgb<>), typeof(SAbgr<>),
        typeof(LGray<>), typeof(LGrayAlpha<>), typeof(LRgb<>), typeof(LRgba<>),
        typeof(LBgr<>), typeof(LBgra<>), typeof(LArgb<>), typeof(LAbgr<>),
    ];

    /// <summary>Checks every component constructor position, including alpha, for nonfinite values.</summary>
    [Fact]
    public void EveryLayoutRejectsEveryNonfiniteComponentButAcceptsRawHdrAndAlpha()
    {
        foreach (Type layout in _layouts)
        {
            Type closed = layout.MakeGenericType(typeof(double));
            ConstructorInfo constructor = Assert.Single(closed.GetConstructors());
            int count = constructor.GetParameters().Length;
            object[] raw = Enumerable.Repeat<object>(-1234d, count).ToArray();
            object value = constructor.Invoke(raw);
            foreach (PropertyInfo property in closed.GetProperties())
            {
                Assert.Equal(-1234d, property.GetValue(value));
                Assert.Null(property.SetMethod);
            }

            for (int component = 0; component < count; component++)
            {
                foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
                {
                    object[] arguments = Enumerable.Repeat<object>(0d, count).ToArray();
                    arguments[component] = invalid;
                    TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => constructor.Invoke(arguments));
                    Assert.IsType<ArgumentOutOfRangeException>(exception.InnerException);
                }
            }
        }
    }

    /// <summary>Inspects constructors, properties, fields, events, interfaces, and all generic constraints.</summary>
    [Fact]
    public void EntirePublicSurfaceHasNoHelperOrOldLayoutEnumLeak()
    {
        Type[] exported = typeof(SRgba<>).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == typeof(SRgba<>).Namespace).ToArray();
        Assert.DoesNotContain(exported, type => type.Name == "ColorPixelFormat");
        foreach (Type type in exported)
        {
            CheckType(type);
            foreach (Type contract in type.GetInterfaces())
            {
                CheckType(contract);
            }

            foreach (Type argument in type.GetGenericArguments())
            {
                CheckConstraints(argument);
            }

            const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            foreach (ConstructorInfo constructor in type.GetConstructors(Flags))
            {
                CheckParameters(constructor);
            }

            foreach (MethodInfo method in type.GetMethods(Flags))
            {
                CheckType(method.ReturnType);
                CheckParameters(method);
                foreach (Type argument in method.GetGenericArguments())
                {
                    CheckConstraints(argument);
                }
            }

            foreach (PropertyInfo property in type.GetProperties(Flags))
            {
                CheckType(property.PropertyType);
                foreach (ParameterInfo parameter in property.GetIndexParameters())
                {
                    CheckType(parameter.ParameterType);
                }
            }

            foreach (FieldInfo field in type.GetFields(Flags))
            {
                CheckType(field.FieldType);
            }

            foreach (EventInfo notification in type.GetEvents(Flags))
            {
                CheckType(notification.EventHandlerType!);
            }
        }

        foreach (Type layout in _layouts.Concat(new[] { typeof(SHsla<>), typeof(SHsva<>) }))
        {
            Type argument = Assert.Single(layout.GetGenericArguments());
            Assert.Equal(GenericParameterAttributes.None, argument.GenericParameterAttributes);
            Assert.Equal(typeof(INumber<>).MakeGenericType(argument), Assert.Single(argument.GetGenericParameterConstraints()));
            Assert.DoesNotContain(argument.GetCustomAttributesData(), attribute => attribute.AttributeType.Name == "IsUnmanagedAttribute");
        }
    }

    /// <summary>Exercises all raw layout alpha boundaries without conflating byte codes with normalized one.</summary>
    [Fact]
    public void RawAlphaIsExplicitAndNormalizationSuppliesOnlyMissingAlpha()
    {
        var expected = new SRgba<double>(0.2, 0.4, 0.6, 1);
        Assert.Equal(expected, new SRgb<byte>(51, 102, 153).NormalizeUnorm<double>(255));
        Assert.Equal(expected, new SBgr<byte>(153, 102, 51).NormalizeUnorm<double>(255));
        Assert.Equal(expected, new SRgba<byte>(51, 102, 153, 255).NormalizeUnorm<double>(255));
        Assert.Equal(expected, new SBgra<byte>(153, 102, 51, 255).NormalizeUnorm<double>(255));
        Assert.Equal(expected, new SArgb<byte>(255, 51, 102, 153).NormalizeUnorm<double>(255));
        Assert.Equal(expected, new SAbgr<byte>(255, 153, 102, 51).NormalizeUnorm<double>(255));
        Assert.Equal(new SRgba<double>(0.2, 0.2, 0.2, 1), new SGray<byte>(51).NormalizeUnorm<double>(255));
        Assert.Equal(new SRgba<double>(0.2, 0.2, 0.2, 0.4), new SGrayAlpha<byte>(51, 102).NormalizeUnorm<double>(255));
        Assert.Equal((byte)255, new SBgr<byte>(3, 2, 1).ToRgba(255).A);
        Assert.Equal((byte)255, new SRgb<byte>(1, 2, 3).ToRgba(255).A);
        foreach (Type layout in new[] { typeof(SGray<byte>), typeof(SRgb<byte>), typeof(SBgr<byte>) })
        {
            Assert.Null(layout.GetMethod("ToRgba", Type.EmptyTypes));
        }
    }

    /// <summary>Checks exact-model semantics without promising a lossless round trip through rounded model storage.</summary>
    [Fact]
    public void TypedModelsRoundOnlyFinalComponentsAndDoNotPromiseLosslessRoundTrips()
    {
        var nearWhite = new SRgba<float>(1, 1, float.BitDecrement(1), 0.5f);
        SHsla<float> rounded = nearWhite.ToHslSrgbDegrees<float>();
        Assert.Equal(new SHsla<float>(60, 1, 1, 0.5f), rounded);
        Assert.Equal(new SRgba<float>(1, 1, 1, 0.5f), rounded.ToRgba<float>());
        Assert.NotEqual(nearWhite, rounded.ToRgba<float>());
        var exact = new SRgba<decimal>(1, 0.5m, 0, 0.25m);
        Assert.Equal(new SHsla<decimal>(30, 1, 0.5m, 0.25m), exact.ToHslSrgbDegrees<decimal>());
        Assert.Equal(new SHsva<decimal>(30, 1, 1, 0.25m), exact.ToHsvSrgbDegrees<decimal>());
        Assert.Equal(exact, exact.ToHslSrgbDegrees<decimal>().ToRgba<decimal>());
        Assert.Equal(exact, exact.ToHsvSrgbDegrees<decimal>().ToRgba<decimal>());
        Assert.Throws<ArithmeticException>(() => exact.ToHslSrgbDegrees<int>());
        Assert.Equal(new SRgba<double>(1, 1, 1, 1), new SHsla<byte>(255, 0, 1, 1).ToRgba<double>());
    }

    /// <summary>Checks raw packed ownership, bit preservation, and explicit normalized conversion for all five formats.</summary>
    [Fact]
    public void PackedWordsAreRawAndNeverImplicitNormalizedComponents()
    {
        CheckPacked(new RawRgb565(0x1234));
        CheckPacked(new RawRgba4444(0x1234));
        CheckPacked(new RawRgba5551(0x1234));
        CheckPacked(new RawRgba1010102(0x12345678));
        CheckPacked(new RawBgra1010102(0x12345678));
        Assert.Throws<ArgumentOutOfRangeException>(() => RawRgb565.FromSrgb(new SRgba<double>(1, 0, 0, 0.5)));
        Assert.Throws<ArithmeticException>(() => new RawRgba4444(0x1234).InterpretSrgb<int>());
        Assert.Equal(new SRgba<int>(1, 1, 1, 1), new RawRgba4444(ushort.MaxValue).InterpretSrgb<int>());
        Assert.Equal(new SRgba<double>(1, 0, 0, 1), new RawRgb565(0xF800).InterpretSrgb<double>());
        Assert.Equal(new SRgba<double>(1, 0, 0, 1), new RawRgba1010102(0xC00003FF).InterpretSrgb<double>());
        Assert.Equal(new SRgba<double>(1, 0, 0, 1), new RawBgra1010102(0xFFF00000).InterpretSrgb<double>());
    }

    private static void CheckPacked<TPixel>(TPixel pixel)
        where TPixel : IPackedColorPixel<TPixel>
    {
        foreach (ColorByteOrder order in Enum.GetValues<ColorByteOrder>())
        {
            byte[] bytes = Enumerable.Repeat((byte)0xA5, (2 * TPixel.ByteSize) + 1).ToArray();
            Color.WritePackedPixels<TPixel>(new[] { pixel, pixel }, bytes, order);
            Assert.Equal((byte)0xA5, bytes[^1]);
            var decoded = new TPixel[2];
            Color.ReadPackedPixels<TPixel>(bytes, decoded, order);
            Assert.Equal(new[] { pixel, pixel }, decoded);
        }

        Assert.DoesNotContain(typeof(TPixel).GetInterfaces(), contract => contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IColorPixel<>));
    }

    private static void CheckParameters(MethodBase member)
    {
        foreach (ParameterInfo parameter in member.GetParameters())
        {
            CheckType(parameter.ParameterType);
        }
    }

    private static void CheckConstraints(Type argument)
    {
        foreach (Type constraint in argument.GetGenericParameterConstraints())
        {
            CheckType(constraint);
        }
    }

    private static void CheckType(Type type)
    {
        if (type.HasElementType)
        {
            CheckType(type.GetElementType()!);
            return;
        }

        if (type.IsGenericParameter)
        {
            return;
        }

        Assert.True(type.IsVisible, type.ToString());
        Assert.DoesNotContain("Rational", type.Name);
        Assert.DoesNotContain("TypedPixelLayout", type.Name);
        Assert.NotEqual("ColorPixelFormat", type.Name);
        Assert.DoesNotContain(type.Name.Split('`')[0], new[] { "Rgba", "Rgb", "Gray", "GrayAlpha", "Bgr", "Bgra", "Argb", "Abgr", "Hsla", "Hsva", "RawRgba", "Srgb8" });
        foreach (Type argument in type.GetGenericArguments())
        {
            CheckType(argument);
        }
    }
}
