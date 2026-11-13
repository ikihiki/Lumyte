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
        Assert.Equal(new[] { "Color", "ColorByteOrder", "ColorPixelFormat", "Rgba`1" }, exported);
        string[] methods = typeof(ColorMath).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Select(method => method.Name).Order().ToArray();
        Assert.Equal(new[] { "DecodePixels", "InterpolateSrgbLinearPremultipliedRaw", "WritePixels" }, methods);
        Assert.Equal(27, Enum.GetValues<ColorPixelFormat>().Length);
        Assert.DoesNotContain(typeof(Rgba<>).GetGenericArguments()[0].GetCustomAttributesData(), attribute => attribute.AttributeType.Name == "IsUnmanagedAttribute");
        foreach (Type type in typeof(Rgba<>).Assembly.GetExportedTypes().Where(type => type.Namespace == "Lumyte.Mathematics.Colors"))
        {
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
        foreach (Type argument in type.GetGenericArguments())
        {
            AssertPublic(argument);
        }
    }
}
