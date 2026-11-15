using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shaders;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Compiles active root declarations and their logical wire schemas.</summary>
public sealed class ShaderSourcePreparationTests
{
    /// <summary>Aliases and mutually exclusive declarations retain the compiler-selected schema.</summary>
    /// <param name="target">The native compiler target.</param>
    /// <param name="name">The application root identifier.</param>
    /// <returns>The completed compilation and artifact round-trip.</returns>
    [Theory]
    [InlineData(ShaderTarget.SpirV, "arguments")]
    [InlineData(ShaderTarget.Wgsl, "arguments")]
    [InlineData(ShaderTarget.Dxil, "arguments")]
    [InlineData(ShaderTarget.SpirV, "customPayload")]
    [InlineData(ShaderTarget.Wgsl, "customPayload")]
    public async Task ActiveAliasedRootsCompileAsync(ShaderTarget target, string name)
    {
        string source = $$$"""
            #include "lumyte.slang"
            #if defined(LUMYTE_SPIRV)
            struct NativeRoot { float Header; float3 Position; };
            typedef NativeRoot RootAlias;
            ConstantBuffer<RootAlias> {{{name}}};
            #else
            struct PortableRoot { float Header; float3 Position; uint Extra; };
            typedef PortableRoot RootAlias;
            ConstantBuffer<RootAlias> {{{name}}};
            #endif
            RWByteAddressBuffer output;
            [numthreads(1,1,1)] void main() { output.Store(0, asuint({{{name}}}.Header + dot({{{name}}}.Position, float3(2,3,5)))); }
            """;
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = source, Target = target });
        ShaderTargetData data = new ShaderArtifact(artifact.GetBinary()).GetTarget(target);
        var root = ShaderDataLayout.Root(data, name);
        Assert.Equal(target == ShaderTarget.SpirV ? "NativeRoot" : "PortableRoot", root.Name);
        Assert.Equal(target == ShaderTarget.SpirV ? 16 : 20, root.Size);
        using var reflection = JsonDocument.Parse(data.ReflectionJson);
        Assert.Single(reflection.RootElement.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString() == name);
        Assert.DoesNotContain(reflection.RootElement.GetProperty("parameters").EnumerateArray(), p => p.GetProperty("name").GetString()!.StartsWith("__lumyte_wire_", StringComparison.Ordinal));
    }

    /// <summary>Numeric roots require no application include and keep natural vector offsets.</summary>
    /// <param name="target">The compiler target.</param>
    /// <returns>The completed compiler check.</returns>
    [Theory]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(ShaderTarget.Wgsl)]
    [InlineData(ShaderTarget.Dxil)]
    public async Task HelperFreeNumericRootUsesWireAsync(ShaderTarget target)
    {
        string source = "struct Numeric { float Header; float3 Position; }; typedef Numeric Root; ConstantBuffer<Root> arguments; RWByteAddressBuffer output; [numthreads(1,1,1)] void main() { output.Store(0, asuint(arguments.Header + dot(arguments.Position, float3(2,3,5)))); }";
        string prepared = ShaderSourcePreparation.Prepare(source);
        Assert.Contains("#include \"lumyte.slang\"", prepared);
        Assert.Contains("LumyteRootWire<Root>", prepared);
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = source, Target = target });
        var layout = ShaderDataLayout.Root(artifact.GetTarget(target), "arguments");
        Assert.Equal("Numeric", layout.Name);
        Assert.Equal(16, layout.Size);
    }

    /// <summary>Root aliases retain typed resource target metadata and natural schema discovery.</summary>
    /// <param name="target">The native compiler target.</param>
    /// <returns>The completed artifact verification.</returns>
    [Theory]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(ShaderTarget.Wgsl)]
    public async Task AliasRetainsTypedReferencesAsync(ShaderTarget target)
    {
        string source = "#include \"lumyte.slang\"\nstruct Node { uint Value; }; struct Payload { GpuRWBufferRef<Node> Nodes; }; typedef Payload Alias; ConstantBuffer<Alias> arguments; [numthreads(1,1,1)] void main() { Node value = arguments.Nodes.Load(0); value.Value += 1; arguments.Nodes.Store(0, value); }";
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = source, Target = target });
        ShaderTargetData data = artifact.GetTarget(target);
        var layout = ShaderDataLayout.Root(data, "arguments");
        Assert.Equal("Payload", layout.Name);
        Assert.Equal(16, layout.Size);
        Assert.Equal("Node", ShaderDataLayout.Data(data, "Node").Name);
        using var reflection = JsonDocument.Parse(data.ReflectionJson);
        Assert.Equal("Node", reflection.RootElement.GetProperty("lumyteReferenceTargets").GetProperty("Payload.Nodes").GetString());
    }

    /// <summary>Helper-free rootless source needs no helper resource declarations.</summary>
    [Fact]
    public void RootlessSourceIsUnchanged()
    {
        string source = "struct Plain { uint Value; }; [numthreads(1,1,1)] void main() {}";
        Assert.Equal(source, ShaderSourcePreparation.Prepare(source));
    }

    /// <summary>Qualified aliases and application Load methods do not collide with helpers.</summary>
    /// <param name="target">The compiler target.</param>
    /// <returns>The completed compiler verification.</returns>
    [Theory]
    [InlineData(ShaderTarget.SpirV)]
    [InlineData(ShaderTarget.Wgsl)]
    public async Task QualifiedAliasPreservesUserLoadAsync(ShaderTarget target)
    {
        string source = "namespace App { struct Numeric { float Header; float3 Position; float Load() { return Header; } }; typedef Numeric Root; } ConstantBuffer<App.Root> custom; RWByteAddressBuffer output; [numthreads(1,1,1)] void main() { output.Store(0, asuint(custom.Load() + custom.Position.x)); }";
        ShaderArtifact artifact = await new SlangShaderCompiler().CompileAsync(new() { Source = source, Target = target });
        Assert.Equal(16, ShaderDataLayout.Root(artifact.GetTarget(target), "custom").Size);
    }

    /// <summary>Every reflected wire is restored once, without adding root storage parameters.</summary>
    [Fact]
    public void CompletesIndependentActiveRootsOnce()
    {
        string reflection = """
            {"parameters":[
              {"name":"__lumyte_schema_root_first","type":{"resultType":{"valueType":{"kind":"struct","name":"First","fields":[]}}}},
              {"name":"__lumyte_wire_first","type":{"kind":"constantBuffer","elementType":{"name":"wire"}}},
              {"name":"__lumyte_schema_root_second","type":{"resultType":{"valueType":{"kind":"struct","name":"Second","fields":[]}}}},
              {"name":"__lumyte_wire_second","type":{"kind":"constantBuffer","elementType":{"name":"wire"}}}
            ]}
            """;
        using var completed = JsonDocument.Parse(ShaderSourcePreparation.CompleteReflection(reflection, string.Empty));
        JsonElement parameters = completed.RootElement.GetProperty("parameters");
        Assert.Equal(4, parameters.GetArrayLength());
        Assert.Equal(new[] { "First", "Second" }, parameters.EnumerateArray().Where(p => p.GetProperty("name").GetString() is "first" or "second").Select(p => p.GetProperty("type").GetProperty("elementType").GetProperty("name").GetString()));
    }
}
