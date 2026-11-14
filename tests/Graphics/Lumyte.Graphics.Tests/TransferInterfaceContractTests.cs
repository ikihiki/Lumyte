using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Root and typed-buffer transfers share one public interface.</summary>
public sealed class TransferInterfaceContractTests
{
    /// <summary>No marker alias remains; the same generated class satisfies both public surfaces.</summary>
    [Fact]
    public void ArgumentsAreTheOnlyTransferContract()
    {
        Assert.Null(typeof(IShaderArguments).Assembly.GetType("Lumyte.Graphics.Abstractions.IShaderData"));
        Assert.Equal(typeof(IShaderArguments), Assert.Single(typeof(UnifiedTransferValue).GetInterfaces()));
        Assert.Equal(typeof(UnifiedTransferValue), typeof(IGraphicsShaderDataBuffer<UnifiedTransferValue>).GenericTypeArguments[0]);
        Assert.Equal(typeof(UnifiedTransferValue), typeof(RenderGraphShaderDataBuffer<UnifiedTransferValue>).GenericTypeArguments[0]);
        Assert.Equal(typeof(IShaderArguments), Assert.Single(typeof(IGraphicsShaderDataBuffer<>).GetGenericArguments()[0].GetGenericParameterConstraints()));
        Assert.Equal(typeof(IShaderArguments), Assert.Single(typeof(ShaderDataSlice<>).GetGenericArguments()[0].GetGenericParameterConstraints()));
        Assert.False(typeof(IShaderArguments).IsAssignableFrom(typeof(Vector4)));
        Assert.False(typeof(IShaderArguments).IsAssignableFrom(typeof(uint)));
        Assert.Contains(typeof(IGraphicDevice).GetMethods(), method => method.Name == "CreateShaderDataBuffer" && method.IsGenericMethodDefinition && method.GetGenericArguments()[0].GetGenericParameterConstraints().Contains(typeof(IShaderArguments)));
        Assert.Contains(typeof(IGraphicDevice).GetMethods(), method => method.Name == "CreateShaderDataBuffer" && method.IsGenericMethodDefinition && method.GetGenericArguments()[0].GetGenericParameterConstraints().Contains(typeof(ValueType)));
    }
}
