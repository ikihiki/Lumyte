using System.Reflection;
using Lumyte.Graphics.RenderGraph;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Guards the logical graph API against exposing backend table management.</summary>
public sealed class GraphShaderResourceApiTests
{
    /// <summary>Graph exports must not require plans, reservations, partitions, slots, or table capacities.</summary>
    [Fact]
    public void ExportedGraphContractsHideTableManagement()
    {
        Type[] exports = typeof(RenderGraphContext).Assembly.GetExportedTypes();
        Assert.DoesNotContain(exports, type => type.Name is "RenderGraphArguments" or "RenderGraphArgumentExtensions");
        var visited = new HashSet<Type>();
        foreach (Type type in exports)
        {
            Check(type);
        }

        void Check(Type type)
        {
            if (type.IsByRef || type.IsArray || type.IsPointer)
            {
                Check(type.GetElementType()!);
                return;
            }

            Assert.False(type.Name.Contains("ArgumentTable", StringComparison.Ordinal), type.FullName);
            Assert.DoesNotContain(type.Name, new[] { "ArgumentPartition", "RenderGraphArguments", "RenderGraphArgumentExtensions" });
            foreach (Type argument in type.IsGenericType ? type.GetGenericArguments() : [])
            {
                Check(argument);
            }

            if (!visited.Add(type) || (type.Namespace != typeof(RenderGraphContext).Namespace && type.Name != "ShaderResources"))
            {
                return;
            }

            foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(member.Name, new[] { "GetArguments", "SetArgumentTable", "Materialize", "Seal", "Registrations", "Reserve", "Attach", "Table", "TextureCapacity", "SamplerCapacity", "BufferCapacity" });
                if (member is ConstructorInfo constructor)
                {
                    foreach (ParameterInfo parameter in constructor.GetParameters())
                    {
                        Check(parameter.ParameterType);
                    }
                }
                else if (member is MethodInfo method)
                {
                    Check(method.ReturnType);
                    foreach (ParameterInfo parameter in method.GetParameters())
                    {
                        Check(parameter.ParameterType);
                        Assert.NotEqual("privateBuffers", parameter.Name);
                    }
                }
                else if (member is PropertyInfo property)
                {
                    Check(property.PropertyType);
                }
                else if (member is FieldInfo field)
                {
                    Check(field.FieldType);
                }
            }

            if (type.BaseType is Type parent)
            {
                Check(parent);
            }

            foreach (Type contract in type.GetInterfaces())
            {
                Check(contract);
            }
        }
    }
}
