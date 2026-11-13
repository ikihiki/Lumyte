using System.Reflection;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;
using Lumyte.UI.TextEditing;
using Xunit;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks the compiled public application dependency graph after removal of the drawing-specific color type.</summary>
public sealed class RemovedColorPublicApiTests
{
    private const string RemovedType = "Lumyte.Graphics.Drawing2D.Color2D";

    /// <summary>No affected public member graph, nested type, interface or constraint exposes the removed type.</summary>
    [Fact]
    public void ApplicationDependencyGraphHasNoRemovedColorType()
    {
        var assemblies = new Dictionary<string, Assembly>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>();
        pending.Enqueue(typeof(UiApplicationOptions).Assembly);
        pending.Enqueue(Assembly.Load("Lumyte.UI.Application.Headless"));
        pending.Enqueue(Assembly.Load("Lumyte.Graphics.Drawing2D.RenderGraph"));
        pending.Enqueue(Assembly.Load("Lumyte.Graphics.Text.Parley"));
        pending.Enqueue(typeof(PaintGraph2D).Assembly);
        pending.Enqueue(typeof(ImeCandidateStyle).Assembly);
        pending.Enqueue(typeof(UiAnimationInterpolators).Assembly);
        while (pending.TryDequeue(out Assembly? assembly))
        {
            if (!assemblies.TryAdd(assembly.GetName().Name!, assembly))
            {
                continue;
            }

            Assert.Null(assembly.GetType(RemovedType, throwOnError: false));
            foreach (AssemblyName reference in assembly.GetReferencedAssemblies().Where(name => name.Name!.StartsWith("Lumyte.", StringComparison.Ordinal)))
            {
                pending.Enqueue(Assembly.Load(reference));
            }
        }

        Assert.Contains("Lumyte.UI", assemblies.Keys);
        Assert.Contains("Lumyte.UI.TextEditing", assemblies.Keys);
        Assert.Contains("Lumyte.Graphics.Drawing2D", assemblies.Keys);
        Assert.Contains("Lumyte.Mathematics", assemblies.Keys);
        Assert.Contains("Lumyte.Graphics.Drawing2D.RenderGraph", assemblies.Keys);
        Assert.Contains("Lumyte.Graphics.Text.Parley", assemblies.Keys);
        var inspected = new HashSet<Type>();
        foreach (Assembly assembly in assemblies.Values)
        {
            foreach (Type type in assembly.GetExportedTypes())
            {
                InspectType(type, inspected);
                foreach (ConstructorInfo constructor in type.GetConstructors())
                {
                    foreach (ParameterInfo parameter in constructor.GetParameters())
                    {
                        InspectType(parameter.ParameterType, inspected);
                    }
                }

                foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    InspectType(method.ReturnType, inspected);
                    foreach (Type argument in method.GetGenericArguments())
                    {
                        InspectType(argument, inspected);
                    }

                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        InspectType(parameter.ParameterType, inspected);
                    }
                }

                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    InspectType(property.PropertyType, inspected);
                    foreach (ParameterInfo parameter in property.GetIndexParameters())
                    {
                        InspectType(parameter.ParameterType, inspected);
                    }
                }

                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    InspectType(field.FieldType, inspected);
                }

                foreach (EventInfo item in type.GetEvents(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    InspectType(item.EventHandlerType!, inspected);
                }
            }
        }

        Assert.DoesNotContain(typeof(PaintGraph2D).Assembly.GetExportedTypes().SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)), method => method.Name == "FromSrgbRgba8");
        Assert.Equal(typeof(SRgba<float>), typeof(Lumyte.UI.Compose.Definitions.Rectangle).GetProperty("Fill")!.PropertyType);
        Assert.Equal(typeof(SRgba<float>), typeof(UiApplicationOptions).GetProperty("Background")!.PropertyType);
        Assert.Equal(typeof(SRgba<float>), typeof(ColorStop2D).GetProperty("Color")!.PropertyType);
    }

    private static void InspectType(Type type, HashSet<Type> inspected)
    {
        Assert.NotEqual(RemovedType, type.FullName);
        Assert.NotEqual("Color2D", type.Name);
        if (!inspected.Add(type))
        {
            return;
        }

        if (type.HasElementType)
        {
            InspectType(type.GetElementType()!, inspected);
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            InspectType(argument, inspected);
        }

        if (type.IsGenericParameter)
        {
            foreach (Type constraint in type.GetGenericParameterConstraints())
            {
                InspectType(constraint, inspected);
            }
        }
        else if (type.Assembly.GetName().Name!.StartsWith("Lumyte.", StringComparison.Ordinal))
        {
            if (type.BaseType is { } parent)
            {
                InspectType(parent, inspected);
            }

            foreach (Type contract in type.GetInterfaces())
            {
                InspectType(contract, inspected);
            }
        }
    }
}
