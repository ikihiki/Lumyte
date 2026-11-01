using System.Runtime.CompilerServices;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks sharing, isolation and lifetime of immutable shader metadata.</summary>
public sealed class ShaderDataLayoutCacheTests
{
    /// <summary>Concurrent buffers and root bindings share their initialized layouts.</summary>
    [Fact]
    public void ConcurrentAccessSharesLayouts()
    {
        ShaderArtifact artifact = Artifact(4);
        ShaderTargetData target = artifact.GetTarget(ShaderTarget.Wgsl);
        var data = new ShaderDataLayout[64];
        var roots = new ShaderDataLayout[64];
        Parallel.For(0, data.Length, i =>
        {
            data[i] = ShaderDataLayout.Data(artifact, ShaderTarget.Wgsl, "CacheValue");
            roots[i] = ShaderDataLayout.Root(target, "arguments");
            Assert.True(ShaderDataLayout.HasRoot(target));
            Assert.True(ShaderDataLayout.HasRoot(target, "arguments"));
            Assert.False(ShaderDataLayout.HasRoot(target, "missing"));
        });
        Assert.All(data, layout => Assert.Same(data[0], layout));
        Assert.All(roots, layout => Assert.Same(roots[0], layout));
        Assert.Equal(4, data[0].Size);
        Assert.Equal(4, roots[0].Size);
    }

    /// <summary>Identically named types in different artifacts and targets stay independent.</summary>
    [Fact]
    public void ArtifactsAndTargetsKeepDistinctSchemas()
    {
        ShaderArtifact small = Artifact(4);
        ShaderArtifact large = Artifact(8);
        var before = ShaderDataLayout.Data(small, ShaderTarget.Wgsl, "CacheValue");
        Assert.Equal(8, ShaderDataLayout.Data(large, ShaderTarget.Wgsl, "CacheValue").Size);
        Assert.Same(before, ShaderDataLayout.Data(small, ShaderTarget.Wgsl, "CacheValue"));
        Assert.NotSame(before, ShaderDataLayout.Data(small, ShaderTarget.Dxil, "CacheValue"));
        Assert.Throws<NotSupportedException>(() => ShaderDataLayout.Data(small, ShaderTarget.SpirV, "CacheValue"));
        Assert.Throws<NotSupportedException>(() => ShaderDataLayout.Data(small, ShaderTarget.Wgsl, "missing"));
        Assert.Throws<ArgumentException>(() => ShaderDataLayout.Root(small.GetTarget(ShaderTarget.Wgsl), "missing"));
        ShaderTargetData extracted = small.GetTarget(ShaderTarget.Wgsl);
        extracted.Code[0] = 0;
        Assert.NotEqual(0, small.GetTarget(ShaderTarget.Wgsl).Code[0]);
        ShaderTargetData unsupported = extracted with { ReflectionJson = "{}" };
        Assert.Throws<NotSupportedException>(() => ShaderDataLayout.Data(unsupported, "CacheValue"));
    }

    /// <summary>Changing immutable target metadata creates a separate cached layout.</summary>
    [Fact]
    public void TargetCopiesDoNotAliasLayouts()
    {
        ShaderTargetData target = Artifact(4).GetTarget(ShaderTarget.Wgsl);
        ShaderTargetData copy = target with { ReflectionJson = Artifact(8).GetTarget(ShaderTarget.Wgsl).ReflectionJson };
        Assert.Equal(4, ShaderDataLayout.Data(target, "CacheValue").Size);
        Assert.Equal(8, ShaderDataLayout.Data(copy, "CacheValue").Size);
        Assert.Same(ShaderDataLayout.Data(target, "CacheValue"), ShaderDataLayout.Data(target, "CacheValue"));
    }

    /// <summary>Process-wide caches do not keep abandoned shader owners alive.</summary>
    [Fact]
    public void CacheDoesNotRetainOwners()
    {
        (WeakReference artifact, WeakReference target) = PopulateCache();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(artifact.IsAlive);
        Assert.False(target.IsAlive);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Artifact, WeakReference Target) PopulateCache()
    {
        ShaderArtifact artifact = Artifact(4);
        ShaderTargetData target = artifact.GetTarget(ShaderTarget.Wgsl);
        _ = ShaderDataLayout.Data(artifact, ShaderTarget.Wgsl, "CacheValue");
        _ = ShaderDataLayout.Root(target, "arguments");
        return (new(artifact), new(target));
    }

    private static ShaderArtifact Artifact(int size)
    {
        var type = new
        {
            kind = "struct",
            name = "CacheValue",
            sizes = new[] { new { kind = "uniform", value = size, alignment = 4 } },
            fields = new[] { new { name = "Value", type = new { kind = "scalar", scalarType = "uint32" }, binding = new { offset = 0, size = 4 } } },
        };
        string json = JsonSerializer.Serialize(new
        {
            lumyteAbi = 1,
            parameters = new object[]
            {
                new { name = "arguments", type = new { kind = "constantBuffer", elementType = type } },
                new { name = "__lumyte_schema_CacheValue", type = new { kind = "resource", resultType = new { kind = "pointer", valueType = type } } },
            },
        });
        return ShaderArtifact.PackTargets([
            new(ShaderTarget.Wgsl, ShaderStage.Compute, "main", "test", "row-major", [1], json),
            new(ShaderTarget.Dxil, ShaderStage.Compute, "main", "test", "row-major", [1], json),
        ]);
    }
}
