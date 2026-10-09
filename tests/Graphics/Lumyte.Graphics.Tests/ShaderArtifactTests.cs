using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks complete offline binaries without requiring a GPU.</summary>
public sealed class ShaderArtifactTests
{
    /// <summary>Checks every target and compilation metadata are embedded in the same binary.</summary>
    [Fact]
    public void OfflineBinaryContainsEveryTargetAndMetadata()
    {
        var artifact = ShaderArtifact.LoadEmbedded(typeof(ShaderExercise).Assembly, "Lumyte.Shaders.increment.lshader");
        foreach (ShaderTarget target in Enum.GetValues<ShaderTarget>())
        {
            ShaderTargetData data = artifact.GetTarget(target);
            Assert.Equal(target, data.Target);
            Assert.Equal(ShaderStage.Compute, data.Stage);
            Assert.Equal("main", data.EntryPoint);
            Assert.NotEmpty(data.CompilerVersion);
            Assert.Equal("row-major", data.MatrixLayout);
            Assert.NotEmpty(data.Code);
            Assert.Contains("entryPoints", data.ReflectionJson);
        }
    }

    /// <summary>Checks binary copies do not expose mutable artifact storage.</summary>
    [Fact]
    public void BinaryOwnsItsStorage()
    {
        var original = ShaderArtifact.LoadEmbedded(typeof(ShaderExercise).Assembly, "Lumyte.Shaders.increment.lshader");
        byte[] bytes = original.GetBinary();
        var artifact = new ShaderArtifact(bytes);
        bytes[0] ^= 1;
        Assert.Equal(original.GetBinary(), artifact.GetBinary());
        byte[] returned = artifact.GetBinary();
        returned[0] ^= 1;
        Assert.Equal(original.GetBinary(), artifact.GetBinary());
    }

    /// <summary>Checks missing resources, truncation and format validation.</summary>
    [Fact]
    public void InvalidBinaryIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => ShaderArtifact.LoadEmbedded(typeof(ShaderExercise).Assembly, "missing"));
        Assert.Throws<ArgumentException>(() => new ShaderArtifact(ReadOnlySpan<byte>.Empty));
        var valid = ShaderArtifact.LoadEmbedded(typeof(ShaderExercise).Assembly, "Lumyte.Shaders.increment.lshader");
        byte[] bytes = valid.GetBinary();
        bytes[0] ^= 1;
        Assert.Throws<ArgumentException>(() => new ShaderArtifact(bytes));
        bytes = valid.GetBinary();
        Assert.Throws<ArgumentException>(() => new ShaderArtifact(bytes.AsSpan(0, bytes.Length - 1)));
    }
}
