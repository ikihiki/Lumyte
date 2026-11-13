using System.Reflection;
using Lumyte.Mathematics.Colors;
using Xunit;
using ColorMath = Lumyte.Mathematics.Colors.Color;

namespace Lumyte.Mathematics.Tests;

/// <summary>Guards the consumer-visible color surface against backend helper exposure.</summary>
public sealed class PublicColorApiTests
{
    /// <summary>Only the agreed storage, enums, and batch entry point are exported.</summary>
    [Fact]
    public void ExportedColorTypesAreCohesive()
    {
        string[] exported = typeof(SrgbRgba<>).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == "Lumyte.Mathematics.Colors")
            .Select(type => type.Name).Order().ToArray();
        string[] expected = ["Color", "ColorByteOrder", "ColorComponentEncoding", "CssColors", "IColorPixel`1", "IPackedColorPixel`1", "SrgbRgba8", "RawRgb565", "RawRgba4444", "RawRgba5551", "RawRgba1010102", "RawBgra1010102", "SrgbGray`1", "SrgbGrayAlpha`1", "SrgbRgb`1", "SrgbRgba`1", "SrgbBgr`1", "SrgbBgra`1", "SrgbArgb`1", "SrgbAbgr`1", "LinearGray`1", "LinearGrayAlpha`1", "LinearRgb`1", "LinearRgba`1", "LinearBgr`1", "LinearBgra`1", "LinearArgb`1", "LinearAbgr`1", "SrgbHsla`1", "SrgbHsva`1"];
        Assert.Equal(expected.Order(), exported);
        string[] methods = typeof(ColorMath).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).Order().ToArray();
        Assert.Equal(new[] { "DecodePixels", "InterpolateSrgbLinearPremultipliedRaw", "ReadPackedPixels", "WritePackedPixels", "WritePixels" }, methods);
        Assert.DoesNotContain(typeof(SrgbRgba<>).Assembly.GetExportedTypes(), type => type.Name == "ColorPixelFormat");
        Assert.DoesNotContain(typeof(SrgbRgba<>).GetGenericArguments()[0].GetCustomAttributesData(), attribute => attribute.AttributeType.Name == "IsUnmanagedAttribute");
        foreach (Type type in typeof(SrgbRgba<>).Assembly.GetExportedTypes().Where(type => type.Namespace == "Lumyte.Mathematics.Colors"))
        {
            foreach (Type argument in type.GetGenericArguments())
            {
                Assert.DoesNotContain(argument.GetCustomAttributesData(), attribute => attribute.AttributeType.Name == "IsUnmanagedAttribute");
                foreach (Type constraint in argument.GetGenericParameterConstraints())
                {
                    AssertPublic(constraint);
                }
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                AssertPublic(field.FieldType);
            }

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                AssertPublic(property.PropertyType);
            }

            foreach (ConstructorInfo constructor in type.GetConstructors())
            {
                foreach (ParameterInfo parameter in constructor.GetParameters())
                {
                    AssertPublic(parameter.ParameterType);
                }
            }

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                AssertPublic(method.ReturnType);
                foreach (ParameterInfo parameter in method.GetParameters())
                {
                    AssertPublic(parameter.ParameterType);
                }
            }
        }
    }

    /// <summary>Only the semantic byte type offers normalization by an implicit conversion.</summary>
    [Fact]
    public void ConversionOperatorsDoNotPoisonGenericStorage()
    {
        Assert.True(typeof(SrgbRgba8).IsValueType);
        Assert.False(typeof(SrgbRgba8).IsGenericType);
        Assert.Contains(typeof(SrgbRgba8).GetCustomAttributesData(), attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        Assert.All(typeof(SrgbRgba8).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), property =>
        {
            Assert.Equal(typeof(byte), property.PropertyType);
            Assert.Null(property.SetMethod);
        });
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        MethodInfo[] operators = typeof(SrgbRgba8).GetMethods(Flags)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit").ToArray();
        Assert.Equal(2, operators.Length);
        MethodInfo forward = Assert.Single(operators, method => method.Name == "op_Implicit");
        Assert.False(forward.IsGenericMethod);
        Assert.Equal(typeof(SrgbRgba<float>), forward.ReturnType);
        Assert.Equal(typeof(SrgbRgba8), Assert.Single(forward.GetParameters()).ParameterType);
        MethodInfo reverse = Assert.Single(operators, method => method.Name == "op_Explicit");
        Assert.False(reverse.IsGenericMethod);
        Assert.Equal(typeof(SrgbRgba8), reverse.ReturnType);
        Assert.Equal(typeof(SrgbRgba<float>), Assert.Single(reverse.GetParameters()).ParameterType);
        Assert.DoesNotContain(typeof(SrgbRgba<>).GetMethods(Flags), method => method.Name is "op_Implicit" or "op_Explicit");
        Assert.DoesNotContain(typeof(SrgbRgba<byte>).GetMethods(Flags), method => method.Name is "op_Implicit" or "op_Explicit");
    }

    private static void AssertPublic(Type type)
    {
        if (type.HasElementType)
        {
            AssertPublic(type.GetElementType()!);
            return;
        }

        if (type.IsGenericParameter)
        {
            return;
        }

        Assert.True(type.IsPublic || type.IsNestedPublic, type.FullName);
        Assert.DoesNotContain("Rational", type.Name);
        Assert.NotEqual("ColorPixelFormat", type.Name);
        Assert.DoesNotContain("TypedPixelLayout", type.Name);
        foreach (Type argument in type.GetGenericArguments())
        {
            AssertPublic(argument);
        }
    }
}
