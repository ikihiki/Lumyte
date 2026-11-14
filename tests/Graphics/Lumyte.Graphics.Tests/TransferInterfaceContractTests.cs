using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Root and typed-buffer transfers share one public interface.</summary>
public sealed class TransferInterfaceContractTests
{
    /// <summary>No marker alias remains; the same generated managed struct satisfies both public surfaces.</summary>
    [Fact]
    public void ArgumentsAreTheOnlyTransferContract()
    {
        Assert.Null(typeof(IShaderArguments).Assembly.GetType("Lumyte.Graphics.Abstractions.IShaderData"));
        Assert.Equal(typeof(IShaderArguments), Assert.Single(typeof(UnifiedTransferValue).GetInterfaces()));
        Assert.Equal(typeof(UnifiedTransferValue), typeof(IGraphicsShaderDataBuffer<UnifiedTransferValue>).GenericTypeArguments[0]);
        Assert.Equal(typeof(UnifiedTransferValue), typeof(RenderGraphShaderDataBuffer<UnifiedTransferValue>).GenericTypeArguments[0]);
        foreach (Type surface in new[] { typeof(IGraphicsShaderDataBuffer<>), typeof(ShaderDataSlice<>), typeof(RenderGraphShaderDataBuffer<>) })
        {
            Type parameter = surface.GetGenericArguments()[0];
            Assert.Contains(typeof(IShaderArguments), parameter.GetGenericParameterConstraints());
            Assert.True((parameter.GenericParameterAttributes & System.Reflection.GenericParameterAttributes.NotNullableValueTypeConstraint) != 0);
        }

        Assert.True(typeof(UnifiedTransferValue).IsValueType);
        Assert.True(System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<UnifiedTransferValue>());
        Assert.Null(typeof(ShaderValueSnapshot).GetProperty("DiagnosticSource"));
        Assert.DoesNotContain(typeof(ShaderValueSnapshot).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance), field => typeof(IShaderArguments).IsAssignableFrom(field.FieldType));
        Assert.False(typeof(IShaderArguments).IsAssignableFrom(typeof(Vector4)));
        Assert.False(typeof(IShaderArguments).IsAssignableFrom(typeof(uint)));
        Assert.Contains(typeof(IGraphicDevice).GetMethods(), method => method.Name == "CreateShaderDataBuffer" && method.IsGenericMethodDefinition && method.GetGenericArguments()[0].GetGenericParameterConstraints().Contains(typeof(IShaderArguments)));
        Assert.Contains(typeof(IGraphicDevice).GetMethods(), method => method.Name == "CreateShaderDataBuffer" && method.IsGenericMethodDefinition && method.GetGenericArguments()[0].GetGenericParameterConstraints().Contains(typeof(ValueType)));
    }

    /// <summary>Native backends own concrete streaming writers rather than shared callback adapters.</summary>
    [Fact]
    public void ProductionTransferHasNoSharedCallbackRoute()
    {
        System.Reflection.Assembly shared = typeof(Lumyte.Graphics.Shared.ShaderRootArguments).Assembly;
        foreach (string name in new[] { "ShaderTransferWriter`1", "IShaderTransferDestination", "ShaderRootDestination", "ShaderUploadDestination", "ShaderDataUpload", "ShaderReferenceEncoding" })
        {
            Assert.Null(shared.GetType("Lumyte.Graphics.Shared." + name));
        }

        foreach (Type backend in new[] { typeof(Lumyte.Graphics.Vulkan.VulkanDevice), typeof(Lumyte.Graphics.Wgpu.WgpuDevice) })
        {
            string prefix = backend.Name[..^"Device".Length];
            Type writer = Assert.IsAssignableFrom<Type>(backend.Assembly.GetType(backend.Namespace + "." + prefix + "ShaderValueWriter"));
            Assert.True(writer.IsValueType);
            Assert.False(writer.IsPublic);
            Assert.True(typeof(IShaderValueWriter).IsAssignableFrom(writer));
        }

        foreach (System.Reflection.MethodInfo method in typeof(Lumyte.Graphics.Shared.ShaderRootArguments).GetMethods().Where(method => method.IsStatic))
        {
            Assert.False(method.IsGenericMethodDefinition);
            Assert.DoesNotContain(method.GetParameters(), parameter => parameter.ParameterType == typeof(Func<ShaderValue, byte[]>));
        }
    }
}
