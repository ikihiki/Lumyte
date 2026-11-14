using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Direct3D12.Tests;

/// <summary>Checks the backend-owned writer without requiring a Windows device.</summary>
public sealed class ShaderWriterContractTests
{
    /// <summary>The actual D3D assembly contains an internal value writer, not a shared adapter.</summary>
    [Fact]
    public void WriterIsBackendOwned()
    {
        Type writer = Assert.IsAssignableFrom<Type>(typeof(Direct3D12Device).Assembly.GetType("Lumyte.Graphics.Direct3D12.Direct3D12ShaderValueWriter"));
        Assert.True(writer.IsValueType);
        Assert.False(writer.IsPublic);
        Assert.True(typeof(IShaderValueWriter).IsAssignableFrom(writer));
        Assert.Null(typeof(Direct3D12Device).Assembly.GetType("Lumyte.Graphics.Direct3D12.Direct3D12ShaderReferenceEncoding"));
    }
}
