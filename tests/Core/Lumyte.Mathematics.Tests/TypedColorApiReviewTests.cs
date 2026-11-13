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
        typeof(Gray<>), typeof(GrayAlpha<>), typeof(Rgb<>), typeof(Rgba<>),
        typeof(Bgr<>), typeof(Bgra<>), typeof(Argb<>), typeof(Abgr<>),
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
        Type[] exported = typeof(Rgba<>).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == typeof(Rgba<>).Namespace).ToArray();
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

        foreach (Type layout in _layouts.Concat(new[] { typeof(Hsla<>), typeof(Hsva<>) }))
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
        var expected = new Rgba<double>(0.2, 0.4, 0.6, 1);
        Assert.Equal(expected, new Rgb<byte>(51, 102, 153).NormalizeUnorm<double>(255));
        Assert.Equal(expected, new Bgr<byte>(153, 102, 51).NormalizeUnorm<double>(255));
        Assert.Equal(expected, new Rgba<byte>(51, 102, 153, 255).NormalizeUnorm<double>(255));
        Assert.Equal(expected, new Bgra<byte>(153, 102, 51, 255).NormalizeUnorm<double>(255));
        Assert.Equal(expected, new Argb<byte>(255, 51, 102, 153).NormalizeUnorm<double>(255));
        Assert.Equal(expected, new Abgr<byte>(255, 153, 102, 51).NormalizeUnorm<double>(255));
        Assert.Equal(new Rgba<double>(0.2, 0.2, 0.2, 1), new Gray<byte>(51).NormalizeUnorm<double>(255));
        Assert.Equal(new Rgba<double>(0.2, 0.2, 0.2, 0.4), new GrayAlpha<byte>(51, 102).NormalizeUnorm<double>(255));
        Assert.Equal((byte)255, new Bgr<byte>(3, 2, 1).ToRgba(255).A);
        Assert.Equal((byte)255, new Rgb<byte>(1, 2, 3).ToRgba(255).A);
        foreach (Type layout in new[] { typeof(Gray<byte>), typeof(Rgb<byte>), typeof(Bgr<byte>) })
        {
            Assert.Null(layout.GetMethod("ToRgba", Type.EmptyTypes));
        }
    }

    /// <summary>Checks exact-model semantics without promising a lossless round trip through rounded model storage.</summary>
    [Fact]
    public void TypedModelsRoundOnlyFinalComponentsAndDoNotPromiseLosslessRoundTrips()
    {
        var nearWhite = new Rgba<float>(1, 1, float.BitDecrement(1), 0.5f);
        Hsla<float> rounded = nearWhite.ToHslSrgbDegrees<float>();
        Assert.Equal(new Hsla<float>(60, 1, 1, 0.5f), rounded);
        Assert.Equal(new Rgba<float>(1, 1, 1, 0.5f), rounded.ToRgba<float>());
        Assert.NotEqual(nearWhite, rounded.ToRgba<float>());
        var exact = new Rgba<decimal>(1, 0.5m, 0, 0.25m);
        Assert.Equal(new Hsla<decimal>(30, 1, 0.5m, 0.25m), exact.ToHslSrgbDegrees<decimal>());
        Assert.Equal(new Hsva<decimal>(30, 1, 1, 0.25m), exact.ToHsvSrgbDegrees<decimal>());
        Assert.Equal(exact, exact.ToHslSrgbDegrees<decimal>().ToRgba<decimal>());
        Assert.Equal(exact, exact.ToHsvSrgbDegrees<decimal>().ToRgba<decimal>());
        Assert.Throws<ArithmeticException>(() => exact.ToHslSrgbDegrees<int>());
        Assert.Equal(new Rgba<double>(1, 1, 1, 1), new Hsla<byte>(255, 0, 1, 1).ToRgba<double>());
    }

    /// <summary>Checks raw packed ownership, bit preservation, and explicit normalized conversion for all five formats.</summary>
    [Fact]
    public void PackedWordsAreRawAndNeverImplicitNormalizedComponents()
    {
        CheckPacked(new Rgb565(0x1234));
        CheckPacked(new Rgba4444(0x1234));
        CheckPacked(new Rgba5551(0x1234));
        CheckPacked(new Rgba1010102(0x12345678));
        CheckPacked(new Bgra1010102(0x12345678));
        Assert.Throws<ArgumentOutOfRangeException>(() => Rgb565.FromNormalized(new Rgba<double>(1, 0, 0, 0.5)));
        Assert.Throws<ArithmeticException>(() => new Rgba4444(0x1234).ToNormalized<int>());
        Assert.Equal(new Rgba<int>(1, 1, 1, 1), new Rgba4444(ushort.MaxValue).ToNormalized<int>());
        Assert.Equal(new Rgba<double>(1, 0, 0, 1), new Rgb565(0xF800).ToNormalized<double>());
        Assert.Equal(new Rgba<double>(1, 0, 0, 1), new Rgba1010102(0xC00003FF).ToNormalized<double>());
        Assert.Equal(new Rgba<double>(1, 0, 0, 1), new Bgra1010102(0xFFF00000).ToNormalized<double>());
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
        foreach (Type argument in type.GetGenericArguments())
        {
            CheckType(argument);
        }
    }
}
