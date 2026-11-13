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
        string[] exported = typeof(Rgba<>).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == "Lumyte.Mathematics.Colors")
            .Select(type => type.Name).Order().ToArray();
        string[] expected = ["Abgr`1", "Argb`1", "Bgr`1", "Bgra`1", "Bgra1010102", "Color", "ColorByteOrder", "ColorComponentEncoding", "CssColors", "Gray`1", "GrayAlpha`1", "Hsla`1", "Hsva`1", "IColorPixel`1", "IPackedColorPixel`1", "Rgb`1", "Rgb565", "Rgba`1", "Rgba1010102", "Rgba4444", "Rgba5551", "Srgb8"];
        Assert.Equal(expected.Order(), exported);
        string[] methods = typeof(ColorMath).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).Order().ToArray();
        Assert.Equal(new[] { "DecodePixels", "InterpolateSrgbLinearPremultipliedRaw", "ReadPackedPixels", "WritePackedPixels", "WritePixels" }, methods);
        Assert.DoesNotContain(typeof(Rgba<>).Assembly.GetExportedTypes(), type => type.Name == "ColorPixelFormat");
        Assert.DoesNotContain(typeof(Rgba<>).GetGenericArguments()[0].GetCustomAttributesData(), attribute => attribute.AttributeType.Name == "IsUnmanagedAttribute");
        foreach (Type type in typeof(Rgba<>).Assembly.GetExportedTypes().Where(type => type.Namespace == "Lumyte.Mathematics.Colors"))
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
        Assert.True(typeof(Srgb8).IsValueType);
        Assert.False(typeof(Srgb8).IsGenericType);
        Assert.Contains(typeof(Srgb8).GetCustomAttributesData(), attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        Assert.All(typeof(Srgb8).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), property =>
        {
            Assert.Equal(typeof(byte), property.PropertyType);
            Assert.Null(property.SetMethod);
        });
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        MethodInfo[] operators = typeof(Srgb8).GetMethods(Flags)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit").ToArray();
        Assert.Equal(2, operators.Length);
        MethodInfo forward = Assert.Single(operators, method => method.Name == "op_Implicit");
        Assert.False(forward.IsGenericMethod);
        Assert.Equal(typeof(Rgba<float>), forward.ReturnType);
        Assert.Equal(typeof(Srgb8), Assert.Single(forward.GetParameters()).ParameterType);
        MethodInfo reverse = Assert.Single(operators, method => method.Name == "op_Explicit");
        Assert.False(reverse.IsGenericMethod);
        Assert.Equal(typeof(Srgb8), reverse.ReturnType);
        Assert.Equal(typeof(Rgba<float>), Assert.Single(reverse.GetParameters()).ParameterType);
        Assert.DoesNotContain(typeof(Rgba<>).GetMethods(Flags), method => method.Name is "op_Implicit" or "op_Explicit");
        Assert.DoesNotContain(typeof(Rgba<byte>).GetMethods(Flags), method => method.Name is "op_Implicit" or "op_Explicit");
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
