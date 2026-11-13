using System.Numerics;
using System.Reflection;
using Lumyte.Mathematics.Geometry;
using Xunit;

namespace Lumyte.Mathematics.Tests;

/// <summary>Protects the actual exported geometry boundary, not friend-assembly accessibility.</summary>
public sealed class PublicGeometryApiTests
{
    private static readonly HashSet<string> _forbiddenNames =
    [
        "CoordinateGeometry", "NumericsGeometry", "DrawingGeometry", "AffineGeometry2D",
        "WindingGeometry2D", "GeometryBatch", "Bezier2D", "Polygon2D",
    ];

    /// <summary>Checks every exported type and recursively follows public signature type graphs.</summary>
    [Fact]
    public void ExportedSignaturesDoNotLeakImplementationUtilities()
    {
        Assembly assembly = typeof(Point2D<>).Assembly;
        Type[] exported = assembly.GetExportedTypes();
        Assert.Contains(typeof(Geometry2D), exported);
        Assert.Contains(typeof(Geometry3D), exported);
        var visited = new HashSet<Type>();
        foreach (Type type in exported)
        {
            InspectType(type, visited);
            InspectMembers(type, visited);
        }

        Type[] utilities = exported.Where(type => type.Namespace == typeof(Geometry2D).Namespace && type.IsClass && type.IsAbstract && type.IsSealed).ToArray();
        Assert.Equal(new[] { typeof(Geometry2D), typeof(Geometry3D) }.OrderBy(type => type.FullName), utilities.OrderBy(type => type.FullName));
        Assert.Equal(new[] { typeof(Geometry2D.IStreamingPointSink) }, exported.Where(type => type.DeclaringType == typeof(Geometry2D)));
        Assert.DoesNotContain(exported, type => type.DeclaringType == typeof(Geometry3D));
    }

    /// <summary>Checks flattening mutates the caller's struct sink and appends only endpoints.</summary>
    [Fact]
    public void StreamingStructSinkRetainsStateAcrossCurves()
    {
        CountingSink sink = default;
        Geometry2D.AppendFlattenedQuadraticBcl(Vector2.Zero, Vector2.UnitX, new Vector2(2, 0), 0.1f, ref sink, 10);
        Assert.Equal(1, sink.Count);
        Assert.Equal(new Vector2(2, 0), sink.Last);

        Geometry2D.AppendFlattenedCubicBcl(new Vector2(2, 0), new Vector2(3, 0), new Vector2(4, 0), new Vector2(5, 0), 0.1f, ref sink, 10);
        Assert.Equal(2, sink.Count);
        Assert.Equal(new Vector2(5, 0), sink.Last);
    }

    private static void InspectType(Type type, HashSet<Type> visited)
    {
        if (!visited.Add(type))
        {
            return;
        }

        string simpleName = type.Name;
        int arity = simpleName.IndexOf((char)96);
        Assert.DoesNotContain(arity < 0 ? simpleName : simpleName[..arity], _forbiddenNames);
        if (type.Assembly == typeof(Geometry2D).Assembly && !type.IsGenericParameter)
        {
            InspectMembers(type, visited);
        }

        if (type.DeclaringType is { } declaring)
        {
            InspectType(declaring, visited);
        }

        if (type.HasElementType)
        {
            InspectType(type.GetElementType()!, visited);
        }

        foreach (Type argument in type.GetGenericArguments())
        {
            InspectType(argument, visited);
        }

        if (type.IsGenericParameter)
        {
            foreach (Type constraint in type.GetGenericParameterConstraints())
            {
                InspectType(constraint, visited);
            }
        }

        if (type.BaseType is { } baseType)
        {
            InspectType(baseType, visited);
        }

        foreach (Type contract in type.GetInterfaces())
        {
            InspectType(contract, visited);
        }

        if (type.IsFunctionPointer)
        {
            InspectType(type.GetFunctionPointerReturnType(), visited);
            foreach (Type parameter in type.GetFunctionPointerParameterTypes())
            {
                InspectType(parameter, visited);
            }
        }
    }

    private static void InspectMembers(Type type, HashSet<Type> visited)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        foreach (MethodBase method in type.GetMethods(Flags).Cast<MethodBase>().Concat(type.GetConstructors(Flags)))
        {
            if (method is MethodInfo info)
            {
                InspectType(info.ReturnType, visited);
                foreach (Type argument in info.GetGenericArguments())
                {
                    InspectType(argument, visited);
                }
            }

            foreach (ParameterInfo parameter in method.GetParameters())
            {
                InspectType(parameter.ParameterType, visited);
            }
        }

        foreach (PropertyInfo property in type.GetProperties(Flags))
        {
            InspectType(property.PropertyType, visited);
            foreach (ParameterInfo parameter in property.GetIndexParameters())
            {
                InspectType(parameter.ParameterType, visited);
            }
        }

        foreach (FieldInfo field in type.GetFields(Flags))
        {
            InspectType(field.FieldType, visited);
        }

        foreach (EventInfo item in type.GetEvents(Flags))
        {
            InspectType(item.EventHandlerType!, visited);
        }
    }

    private struct CountingSink : Geometry2D.IStreamingPointSink
    {
        public int Count;

        public Vector2 Last;

        public void Append(Vector2 point)
        {
            Count++;
            Last = point;
        }
    }
}
