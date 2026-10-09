using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks DLL artifact loading without requiring a GPU.</summary>
public sealed class ShaderArtifactTests
{
    /// <summary>Checks both target resources and code ownership.</summary>
    /// <param name="target">The artifact target embedded by the build.</param>
    [Theory]
    [InlineData(ShaderTarget.Wgsl)]
    [InlineData(ShaderTarget.SpirV)]
    public void EmbeddedArtifactsOwnTheirCode(ShaderTarget target)
    {
        var embedded = ShaderArtifact.LoadEmbedded(typeof(ShaderExercise).Assembly, "Lumyte.Shaders.increment", target, ShaderStage.Compute);
        byte[] original = embedded.GetCode();
        byte[] input = (byte[])original.Clone();
        var artifact = new ShaderArtifact(target, ShaderStage.Compute, "main", input, embedded.ReflectionJson);
        input[0] ^= 1;
        Assert.Equal(original, artifact.GetCode());
        byte[] returned = artifact.GetCode();
        returned[0] ^= 1;
        Assert.Equal(original, artifact.GetCode());
        Assert.Equal(target, artifact.Target);
        Assert.Equal("main", artifact.EntryPoint);
        Assert.Contains("entryPoints", artifact.ReflectionJson);
    }

    /// <summary>Checks missing resources and malformed code or reflection before module allocation.</summary>
    [Fact]
    public void InvalidArtifactsFailBeforeModuleCreation()
    {
        System.Reflection.Assembly assembly = typeof(ShaderExercise).Assembly;
        Assert.Throws<InvalidOperationException>(() => ShaderArtifact.LoadEmbedded(assembly, "missing", ShaderTarget.Wgsl, ShaderStage.Compute));
        Assert.Throws<ArgumentException>(() => new ShaderArtifact(ShaderTarget.SpirV, ShaderStage.Compute, "main", new byte[20], "{}"));
        Assert.Throws<ArgumentException>(() => new ShaderArtifact(ShaderTarget.Wgsl, ShaderStage.Compute, "main", ReadOnlySpan<byte>.Empty, "{}"));
        var valid = ShaderArtifact.LoadEmbedded(assembly, "Lumyte.Shaders.increment", ShaderTarget.Wgsl, ShaderStage.Compute);
        Assert.Throws<ArgumentException>(() => new ShaderArtifact(valid.Target, valid.Stage, valid.EntryPoint, valid.GetCode(), "[]"));
    }
}
