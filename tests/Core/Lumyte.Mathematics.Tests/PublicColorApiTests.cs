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
        string[] exported = typeof(SRgba<>).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == "Lumyte.Mathematics.Colors")
            .Select(type => type.Name).Order().ToArray();
        string[] expected = ["Color", "ColorByteOrder", "ColorComponentEncoding", "CssColors", "IColorPixel`1", "IPackedColorPixel`1", "SRgba8", "RawRgb565", "RawRgba4444", "RawRgba5551", "RawRgba1010102", "RawBgra1010102", "SGray`1", "SGrayAlpha`1", "SRgb`1", "SRgba`1", "SBgr`1", "SBgra`1", "SArgb`1", "SAbgr`1", "LGray`1", "LGrayAlpha`1", "LRgb`1", "LRgba`1", "LBgr`1", "LBgra`1", "LArgb`1", "LAbgr`1", "SHsla`1", "SHsva`1"];
        Assert.Equal(expected.Order(), exported);
        string[] methods = typeof(ColorMath).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).Order().ToArray();
        Assert.Equal(new[] { "DecodePixels", "InterpolateSrgbLinearPremultipliedRaw", "ReadPackedPixels", "WritePackedPixels", "WritePixels" }, methods);
        Assert.DoesNotContain(typeof(SRgba<>).Assembly.GetExportedTypes(), type => type.Name == "ColorPixelFormat");
        Assert.DoesNotContain(typeof(SRgba<>).GetGenericArguments()[0].GetCustomAttributesData(), attribute => attribute.AttributeType.Name == "IsUnmanagedAttribute");
        foreach (Type type in typeof(SRgba<>).Assembly.GetExportedTypes().Where(type => type.Namespace == "Lumyte.Mathematics.Colors"))
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
        Assert.True(typeof(SRgba8).IsValueType);
        Assert.False(typeof(SRgba8).IsGenericType);
        Assert.Contains(typeof(SRgba8).GetCustomAttributesData(), attribute => attribute.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");
        Assert.All(typeof(SRgba8).GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), property =>
        {
            Assert.Equal(typeof(byte), property.PropertyType);
            Assert.Null(property.SetMethod);
        });
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly;
        MethodInfo[] operators = typeof(SRgba8).GetMethods(Flags)
            .Where(method => method.Name is "op_Implicit" or "op_Explicit").ToArray();
        Assert.Equal(2, operators.Length);
        MethodInfo forward = Assert.Single(operators, method => method.Name == "op_Implicit");
        Assert.False(forward.IsGenericMethod);
        Assert.Equal(typeof(SRgba<float>), forward.ReturnType);
        Assert.Equal(typeof(SRgba8), Assert.Single(forward.GetParameters()).ParameterType);
        MethodInfo reverse = Assert.Single(operators, method => method.Name == "op_Explicit");
        Assert.False(reverse.IsGenericMethod);
        Assert.Equal(typeof(SRgba8), reverse.ReturnType);
        Assert.Equal(typeof(SRgba<float>), Assert.Single(reverse.GetParameters()).ParameterType);
        Assert.DoesNotContain(typeof(SRgba<>).GetMethods(Flags), method => method.Name is "op_Implicit" or "op_Explicit");
        Assert.DoesNotContain(typeof(SRgba<byte>).GetMethods(Flags), method => method.Name is "op_Implicit" or "op_Explicit");
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
