using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Shaders;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks compiler contracts on CI hosts without requiring a GPU.</summary>
public sealed class ShaderCompilerTests
{
    /// <summary>Checks online compilation for both device targets.</summary>
    /// <param name="target">The requested output target.</param>
    /// <returns>The completion of the compiler checks.</returns>
    [Theory]
    [InlineData(ShaderTarget.Wgsl)]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(null)]
    public async Task CompilerProducesRequestedTargetAsync(ShaderTarget? target)
    {
        IShaderCompiler compiler = new SlangShaderCompiler();
        using Stream source = typeof(ShaderExercise).Assembly.GetManifestResourceStream("Lumyte.Shaders.increment.slang")!;
        using var reader = new StreamReader(source);
        ShaderArtifact artifact = await compiler.CompileAsync(new ShaderCompilationDesc { Source = await reader.ReadToEndAsync(), Target = target });
        ShaderTarget[] expected = target is { } selected ? [selected] : Enum.GetValues<ShaderTarget>();
        foreach (ShaderTarget current in expected)
        {
            ShaderTargetData data = artifact.GetTarget(current);
            Assert.Equal(current, data.Target);
            Assert.Equal(ShaderStage.Compute, data.Stage);
            Assert.Equal("main", data.EntryPoint);
            Assert.NotEmpty(data.Code);
            Assert.Contains("entryPoints", data.ReflectionJson);
            Assert.NotEmpty(data.CompilerVersion);
            var transported = new ShaderArtifact(artifact.GetBinary());
            Assert.Equal(data.Code, transported.GetTarget(current).Code);
        }

        if (target is { } single)
        {
            ShaderTarget missing = single == ShaderTarget.Wgsl ? ShaderTarget.SpirV : ShaderTarget.Wgsl;
            Assert.Throws<NotSupportedException>(() => artifact.GetTarget(missing));
        }
    }

    /// <summary>Checks a single-target artifact carries reference targets, type schema and helper version.</summary>
    /// <param name="target">The selected compiler target.</param>
    /// <returns>The compiler verification.</returns>
    [Theory]
    [InlineData(ShaderTarget.Wgsl)]
    [InlineData(ShaderTarget.SpirV)]
    public async Task CompilerEmbedsReferenceAbiAsync(ShaderTarget target)
    {
        IShaderCompiler compiler = new SlangShaderCompiler();
        using Stream source = typeof(ShaderBindingExercise).Assembly.GetManifestResourceStream("Lumyte.Shaders.binding-compute.slang")!;
        using var reader = new StreamReader(source);
        ShaderArtifact artifact = await compiler.CompileAsync(new() { Source = await reader.ReadToEndAsync(), Target = target });
        using var reflection = System.Text.Json.JsonDocument.Parse(artifact.GetTarget(target).ReflectionJson);
        Assert.Equal(1, reflection.RootElement.GetProperty("lumyteAbi").GetInt32());
        Assert.Equal("BindingNode", reflection.RootElement.GetProperty("lumyteReferenceTargets").GetProperty("BindingComputeArguments.Node").GetString());
        Assert.Contains(reflection.RootElement.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == "__lumyte_schema_BindingNode");
        ShaderTarget missing = target == ShaderTarget.Wgsl ? ShaderTarget.SpirV : ShaderTarget.Wgsl;
        Assert.Throws<NotSupportedException>(() => artifact.GetTarget(missing));
    }

    /// <summary>Checks failed Slang source returns diagnostics.</summary>
    /// <returns>The completion of the compiler checks.</returns>
    [Fact]
    public async Task InvalidSourceReturnsCompilerDiagnosticsAsync()
    {
        IShaderCompiler compiler = new SlangShaderCompiler();
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => compiler.CompileAsync(new ShaderCompilationDesc { Source = "this is not valid Slang;" }));
        Assert.Contains("Slang compilation failed", error.Message);
        Assert.Contains("error", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Checks cancellation before a compiler executable is started.</summary>
    /// <returns>The completion of the compiler checks.</returns>
    [Fact]
    public async Task CancellationPreventsCompilerStartAsync()
    {
        IShaderCompiler compiler = new SlangShaderCompiler("missing-slangc-executable");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => compiler.CompileAsync(new ShaderCompilationDesc { Source = "void main() {}" }, cancellation.Token));
    }
}
