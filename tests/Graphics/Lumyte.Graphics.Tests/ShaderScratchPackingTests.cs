using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Guards the mandatory cursor-only public writing surface.</summary>
public sealed class ShaderScratchPackingTests
{
    /// <summary>No optional capability or snapshot writing backdoor remains.</summary>
    [Fact]
    public void PublicWritingSurfaceRequiresCursor()
    {
        MethodInfo method = Assert.Single(typeof(IShaderValueWriter).GetMethods());
        Assert.Equal("WriteWire", method.Name);
        Assert.True(method.IsAbstract);
        Assert.Empty(typeof(IShaderValueWriter).GetProperties());
        Assert.Equal("Emit", Assert.Single(typeof(IShaderReferenceEmitter).GetMethods()).Name);
        Assert.Single(Assert.Single(typeof(IShaderReferenceEmitter).GetMethods()).GetParameters());
        Assert.DoesNotContain(typeof(ShaderDataLayout).GetMethods(), m => m.Name.StartsWith("Pack", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(ShaderRootArguments).GetMethods(), m => m.Name == "CreateSnapshot");
        Assert.All(typeof(ShaderRootArguments).GetMethods().Where(m => m.Name == "Seal"), m => Assert.Empty(m.GetParameters()));
        Assert.Null(typeof(ShaderBindingSnapshot).GetProperty("Root"));
        Assert.All(typeof(ShaderBindingSnapshot).GetMethods().Where(m => m.Name == "Capture"), m => Assert.Equal(typeof(ShaderRootArguments), m.GetParameters()[0].ParameterType));
        foreach (Type encoder in new[] { typeof(IRenderEncoder), typeof(IComputeEncoder) })
        {
            Assert.DoesNotContain(encoder.GetMethods(), m => m.Name == "SetArguments" && m.GetParameters().Any(p => p.ParameterType == typeof(ShaderValueSnapshot)));
            MethodInfo unmanaged = Assert.Single(encoder.GetMethods(), m => m.Name == "SetArguments" && m.GetParameters().Length == 3);
            Assert.True(unmanaged.IsAbstract);
        }

        MethodInfo create = Assert.Single(typeof(ShaderRootArguments).GetMethods(), m => m.Name == "Create");
        Assert.Equal(typeof(Func<ShaderValue, byte[]>), create.GetParameters()[2].ParameterType);
        Assert.False(create.GetParameters()[2].IsOptional);
    }
}
