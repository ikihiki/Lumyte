using System.Reflection;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;
using Lumyte.UI.TextEditing;
using Xunit;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks named color-space ownership in the compiled public dependency graph.</summary>
public sealed class SpacePublicApiTests
{
    private static readonly HashSet<string> _forbiddenNames = ["Rgba`1", "Rgb`1", "Gray`1", "GrayAlpha`1", "Bgr`1", "Bgra`1", "Argb`1", "Abgr`1", "Hsla`1", "Hsva`1", "Srgb8", "Rgb565", "Rgba4444", "Rgba5551", "Rgba1010102", "Bgra1010102", "Color2D", "SrgbGray`1", "LinearGray`1", "SrgbGrayAlpha`1", "LinearGrayAlpha`1", "SrgbRgb`1", "LinearRgb`1", "SrgbRgba`1", "LinearRgba`1", "SrgbBgr`1", "LinearBgr`1", "SrgbBgra`1", "LinearBgra`1", "SrgbArgb`1", "LinearArgb`1", "SrgbAbgr`1", "LinearAbgr`1", "SrgbHsla`1", "SrgbHsva`1", "SrgbRgba8"];

    /// <summary>Only explicitly space-named or raw-word data types escape through public member graphs.</summary>
    [Fact]
    public void PublicGraphsDoNotExposeAmbiguousColorComponents()
    {
        var assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var queue = new Queue<Assembly>();
        queue.Enqueue(typeof(SRgba<>).Assembly);
        queue.Enqueue(typeof(UiApplicationOptions).Assembly);
        queue.Enqueue(typeof(PaintGraph2D).Assembly);
        queue.Enqueue(typeof(ImeCandidateStyle).Assembly);
        queue.Enqueue(Assembly.Load("Lumyte.Graphics.Text.Parley"));
        queue.Enqueue(Assembly.Load("Lumyte.Graphics.Drawing2D.RenderGraph"));
        var visited = new HashSet<Type>();
        while (queue.TryDequeue(out Assembly? assembly))
        {
            if (!assemblies.TryAdd(assembly.GetName().Name!, assembly))
            {
                continue;
            }

            foreach (AssemblyName reference in assembly.GetReferencedAssemblies().Where(value => value.Name!.StartsWith("Lumyte.", StringComparison.Ordinal)))
            {
                queue.Enqueue(Assembly.Load(reference));
            }

            foreach (Type type in assembly.GetExportedTypes())
            {
                Inspect(type, visited);
                foreach (ConstructorInfo constructor in type.GetConstructors())
                {
                    foreach (ParameterInfo parameter in constructor.GetParameters())
                    {
                        Inspect(parameter.ParameterType, visited);
                    }
                }

                const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                foreach (MethodInfo method in type.GetMethods(Flags))
                {
                    Inspect(method.ReturnType, visited);
                    foreach (Type argument in method.GetGenericArguments())
                    {
                        Inspect(argument, visited);
                    }

                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        Inspect(parameter.ParameterType, visited);
                    }
                }

                foreach (PropertyInfo property in type.GetProperties(Flags))
                {
                    Inspect(property.PropertyType, visited);
                    foreach (ParameterInfo parameter in property.GetIndexParameters())
                    {
                        Inspect(parameter.ParameterType, visited);
                    }
                }

                foreach (FieldInfo field in type.GetFields(Flags))
                {
                    Inspect(field.FieldType, visited);
                }

                foreach (EventInfo item in type.GetEvents(Flags))
                {
                    Inspect(item.EventHandlerType!, visited);
                }
            }
        }
    }

    /// <summary>Drawing inputs and palette normalization select encoded sRGB, never an implicit transfer.</summary>
    [Fact]
    public void DrawingAndPaletteUseEncodedTypesOnly()
    {
        Assert.Equal(typeof(SRgba<float>), typeof(Lumyte.UI.Compose.Definitions.Rectangle).GetProperty("Fill")!.PropertyType);
        Assert.Equal(typeof(SRgba<float>), typeof(RenderTarget2D).GetProperty("ClearColor")!.PropertyType);
        Assert.Equal(typeof(SRgba<float>), typeof(PaintGraph2D).GetMethod("Solid")!.GetParameters()[0].ParameterType);
        Assert.Equal(typeof(SRgba<float>), Assert.Single(typeof(SRgba8).GetMethods(), method => method.Name == "op_Implicit").ReturnType);
        Assert.False(typeof(SRgba<float>).IsAssignableFrom(typeof(LRgba<float>)));
        string[] linearOnly = ["PremultiplyLinear", "UnpremultiplyLinear", "SourceOverLinearPremultiplied", "AddLinearPremultiplied", "AddUnitLinearPremultiplied", "ApplyMatrixLinearPremultiplied", "ToLinearGrayscale"];
        Assert.DoesNotContain(typeof(SRgba<float>).GetMethods(), method => linearOnly.Contains(method.Name));
        Assert.All(linearOnly, name => Assert.Contains(typeof(LRgba<float>).GetMethods(), method => method.Name == name));
        foreach (Type type in new[] { typeof(SRgba<float>), typeof(LRgba<float>) })
        {
            Assert.DoesNotContain(type.GetMethods(), method => method.Name == "op_Implicit");
        }
    }

    private static void Inspect(Type type, HashSet<Type> visited)
    {
        if (type.Namespace == "Lumyte.Mathematics.Colors" || type.Name == "Color2D")
        {
            Assert.DoesNotContain(type.Name, _forbiddenNames);
            Assert.True(type.IsPublic || type.IsNestedPublic || type.IsGenericParameter, type.ToString());
        }

        if (!visited.Add(type))
        {
            return;
        }

        if (type.HasElementType)
        {
            Inspect(type.GetElementType()!, visited);
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            Inspect(argument, visited);
        }

        if (type.IsGenericParameter)
        {
            foreach (Type constraint in type.GetGenericParameterConstraints())
            {
                Inspect(constraint, visited);
            }
        }
        else if (type.Assembly.GetName().Name!.StartsWith("Lumyte.", StringComparison.Ordinal))
        {
            foreach (Type contract in type.GetInterfaces())
            {
                Inspect(contract, visited);
            }

            if (type.BaseType is { } parent)
            {
                Inspect(parent, visited);
            }
        }
    }
}
