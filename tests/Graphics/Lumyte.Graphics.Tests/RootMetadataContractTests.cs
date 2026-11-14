using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Guards metadata-only common roots independently of any backend storage implementation.</summary>
public sealed class RootMetadataContractTests
{
    /// <summary>No common storage owner or compatibility lifecycle remains.</summary>
    [Fact]
    public void CommonRootHasNoStorageOrLifecycle()
    {
        Type root = typeof(ShaderRootArguments);
        Assert.Null(typeof(IShaderArguments).Assembly.GetType("Lumyte.Graphics.Abstractions.IShaderRootStorage"));
        Assert.True(root.IsSealed);
        Assert.False(typeof(IDisposable).IsAssignableFrom(root));
        Assert.Null(root.GetProperty("Storage"));
        foreach (string name in new[] { "Allocate", "Seal", "Dispose" })
        {
            Assert.DoesNotContain(root.GetMethods(), method => method.Name == name);
        }

        MethodInfo create = Assert.Single(root.GetMethods(BindingFlags.Static | BindingFlags.Public), method => method.Name == "Create");
        Type[] expected =
        [
            typeof(string), typeof(string), typeof(int), typeof(IShaderReferenceMetadata),
            typeof(Func<IShaderReferenceMetadata, Action<IReadOnlyCollection<IShaderReference>>>),
        ];
        Assert.Equal(expected, create.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.True(create.GetParameters()[4].IsOptional);
    }

    /// <summary>Readonly fields admit only names, logical length, reference metadata and admission.</summary>
    [Fact]
    public void CommonRootFieldsCannotRetainNumericPayloadOrOpaqueOwner()
    {
        Type[] allowed = [typeof(string), typeof(int), typeof(IShaderReferenceMetadata), typeof(Action<IReadOnlyCollection<IShaderReference>>)];
        FieldInfo[] fields = typeof(ShaderRootArguments).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.Equal(5, fields.Length);
        Assert.All(fields, field =>
        {
            Assert.True(field.IsInitOnly, field.Name);
            Assert.Contains(field.FieldType, allowed);
        });
        Assert.All(typeof(ShaderRootArguments).GetProperties(), property => Assert.Null(property.SetMethod));
    }
}
