using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Guards the mandatory flat sequential public writing surface.</summary>
public sealed class ShaderScratchPackingTests
{
    /// <summary>No optional capability or snapshot writing backdoor remains.</summary>
    [Fact]
    public void PublicWritingSurfaceRequiresFlatSequentialValues()
    {
        MethodInfo[] methods = typeof(IShaderValueWriter).GetMethods();
        Assert.Equal(new[] { "WriteReference", "WriteValue" }, methods.Select(method => method.Name).Order());
        Assert.All(methods, method =>
        {
            Assert.True(method.IsAbstract);
            Assert.Equal(typeof(void), method.ReturnType);
            Assert.Single(method.GetParameters());
        });
        MethodInfo raw = Assert.Single(methods, method => method.Name == "WriteValue");
        Assert.False(raw.IsGenericMethod);
        Assert.Equal(typeof(ReadOnlySpan<byte>), raw.GetParameters()[0].ParameterType);
        MethodInfo reference = Assert.Single(methods, method => method.Name == "WriteReference");
        Type argument = Assert.Single(reference.GetGenericArguments());
        Assert.Equal(typeof(IGpuRef<>).MakeGenericType(argument), reference.GetParameters()[0].ParameterType);
        Assert.Empty(typeof(IShaderValueWriter).GetProperties());
        foreach (string name in new[] { "ShaderWire", "ShaderWireCursor`1", "IShaderReferenceCursor", "IShaderReferenceEmitter", "IShaderWireSink" })
        {
            Assert.Null(typeof(IShaderValueWriter).Assembly.GetType($"Lumyte.Graphics.Abstractions.{name}"));
        }

        Assert.DoesNotContain(methods, method => method.Name == "WriteWire");
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
        Assert.False(create.IsGenericMethod);
        Assert.Equal(typeof(int), create.GetParameters()[2].ParameterType);
        Assert.Equal(typeof(IShaderRootStorage), create.GetParameters()[3].ParameterType);
        Assert.Equal(typeof(IShaderReferenceMetadata), create.GetParameters()[4].ParameterType);
        Assert.DoesNotContain(create.GetParameters(), parameter => parameter.ParameterType == typeof(Func<ShaderValue, byte[]>));
        Assert.False(create.GetParameters()[3].IsOptional);
        Assert.False(create.GetParameters()[4].IsOptional);
    }
}
