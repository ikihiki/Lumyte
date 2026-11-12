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

    /// <summary>Graph callers do not construct root shims or extract pending registration references.</summary>
    [Fact]
    public void PublicRegistrationWrappersAreAbsent()
    {
        Type[] types = typeof(Lumyte.Graphics.Abstractions.ShaderResources).Assembly.GetExportedTypes()
            .Concat(typeof(DrawingArgumentPlan2D).Assembly.GetExportedTypes()).ToArray();
        Assert.DoesNotContain(types, type => type.IsGenericTypeDefinition && (type.Name == "ArgumentRegistration`1" || type.Name == "DrawingArgumentRegistration2D`1"));
        Type[] graphTypes = typeof(RenderGraphContext).Assembly.GetExportedTypes();
        Assert.Contains(graphTypes, type => type.Name == "RenderGraphRenderEncoder");
        Assert.Contains(graphTypes, type => type.Name == "RenderGraphComputeEncoder");
    }

    /// <summary>Logical graph inputs and encoders never expose native registration or root-argument shims.</summary>
    [Fact]
    public void LogicalGraphInputsAndEncodersHaveNoNativeEscape()
    {
        Type inputs = typeof(RenderGraphShaderResources);
        Assert.Equal(typeof(object), inputs.BaseType);
        foreach (MethodInfo method in inputs.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            Assert.DoesNotContain(method.Name, new[] { "Draw", "DrawIndexed", "Dispatch", "RegisterTexture", "RegisterBuffer", "RegisterSampler", "RegisterShaderData", "Snapshot", "Core", "Reference" });
            Assert.Equal(typeof(RenderGraphShaderResources), method.ReturnType);
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                Assert.False(parameter.ParameterType.Name.StartsWith("IGpuRef", StringComparison.Ordinal));
            }
        }

        foreach (Type encoder in typeof(RenderGraphContext).Assembly.GetExportedTypes().Where(type => type.Name is "RenderGraphRenderEncoder" or "RenderGraphComputeEncoder"))
        {
            Assert.DoesNotContain(encoder.GetInterfaces(), type => type == typeof(Lumyte.Graphics.Abstractions.IRenderEncoder) || type == typeof(Lumyte.Graphics.Abstractions.IComputeEncoder));
            foreach (MemberInfo member in encoder.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                Assert.DoesNotContain(member.Name, new[] { "SetArgumentTable", "Underlying", "Native", "Commands", "Reference", "Table" });
                if (member is MethodInfo method)
                {
                    Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType.Name.Contains("ShaderValueSnapshot", StringComparison.Ordinal) || parameter.ParameterType.Name.Contains("IShaderArguments", StringComparison.Ordinal));
                }
            }
        }
    }
}
