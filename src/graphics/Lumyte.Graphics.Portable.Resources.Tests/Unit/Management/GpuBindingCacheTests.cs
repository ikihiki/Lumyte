using Lumyte.Graphics.Portable.Shaders;

namespace Lumyte.Graphics.Portable.Resources.Tests.Unit.Management;

public sealed class GpuBindingCacheTests
{
    internal static readonly GpuTextureDescription TextureDescription = new(GpuTextureDimension.Texture2D, 2, 2, 1, 1, 1, 1,
        GpuFormat.Rgba8Unorm, GpuTextureUsage.Sampled | GpuTextureUsage.CopySource | GpuTextureUsage.CopyDestination);
    internal static PortableShaderProgram LoadProgram(ManagerTestBackend backend)
    {
        var group = new PortableShaderGroupLayout([
            new(0, GpuShaderStage.Compute, new GpuBufferBindingLayout(GpuBufferBindingType.Storage)),
            new(1, GpuShaderStage.Compute, new GpuTextureBindingLayout(GpuTextureSampleType.Float)),
            new(2, GpuShaderStage.Compute, new GpuSamplerBindingLayout(GpuSamplerBindingType.Filtering))]);
        var schema = new PortableShaderBindingSchema([
            new("Values", 0, 0, GpuBindingLayoutKind.Buffer), new("Image", 0, 1, GpuBindingLayoutKind.Texture),
            new("Filter", 0, 2, GpuBindingLayoutKind.Sampler)]);
        return new PortableShaderLoader(backend).Load(new(PortableShaderPackage.CurrentVersion, "@compute @workgroup_size(1) fn main() {}",
            [new(GpuShaderStage.Compute, "main")], PortableShaderFeatures.None, [group], null, [], schema, "managed-inputs-v1"));
    }
    internal sealed record Inputs(GpuBufferRef Buffer, GpuViewRef View, GpuSamplerRef Sampler, bool Reverse = false) : IGpuBindingInputs
    {
        public void Write(GpuBindingWriter writer)
        {
            if (Reverse) { writer.Sampler(2, Sampler); writer.Texture(1, View); writer.Buffer(0, Buffer, 4, 8); }
            else { writer.Buffer(0, Buffer, 4, 8); writer.Texture(1, View); writer.Sampler(2, Sampler); }
        }
    }
    internal static Inputs CreateInputs(GpuResourceScope scope)
    {
        GpuBufferRef buffer = scope.CreateBuffer(new(16, GpuBufferUsage.Storage));
        GpuTextureRef texture = scope.CreateTexture(TextureDescription);
        return new(buffer, scope.GetView(texture), scope.GetSampler(new()));
    }

    [Fact]
    public async Task EquivalentInputsReuseImmutableBindingsRegardlessOfWriteOrder()
    {
        var backend = new ManagerTestBackend(); using var program = LoadProgram(backend);
        await using var manager = new GpuResourceManager(backend); using var scope = manager.CreateScope();
        Inputs inputs = CreateInputs(scope);

        GpuBindingsRef first = scope.GetBindings(program, 0, inputs);
        GpuBindingsRef reordered = scope.GetBindings(program, 0, inputs with { Reverse = true });

        Assert.Same(first, reordered);
        ManagerTestBackend.Bindings raw = Assert.Single(backend.CreatedBindings);
        Assert.Collection(raw.Entries,
            entry => Assert.Equal(new GpuBufferRange(manager.GetBufferRange(inputs.Buffer).Buffer, 4, 8), entry.BufferRange),
            entry => Assert.Equal(manager.GetTextureView(inputs.View), entry.TextureView),
            entry => Assert.Equal(inputs.Sampler.Description, entry.SamplerDescription));
    }

    [Fact]
    public async Task DefaultViewUsesItsTextureOwnershipWithoutACycle()
    {
        var backend = new ManagerTestBackend(); await using var manager = new GpuResourceManager(backend);
        var scope = manager.CreateScope(); GpuTextureRef texture = scope.CreateTexture(TextureDescription);
        GpuViewRef first = scope.GetView(texture); GpuViewRef explicitDefault = scope.GetView(texture, default(GpuTextureViewDescription).Normalize(TextureDescription));
        using GpuResourcePin pin = manager.Pin(first);

        scope.Dispose(); manager.Trim();

        Assert.Same(first, explicitDefault);
        Assert.Equal(0, manager.Statistics.ViewCount);
        Assert.Empty(backend.Destroyed);
        pin.Dispose(); manager.Trim(); Assert.Single(backend.Destroyed);
    }

    [Fact]
    public async Task BindingOwnershipRetainsAllItsResourcesAfterScopeDisposal()
    {
        var backend = new ManagerTestBackend(); using var program = LoadProgram(backend);
        await using var manager = new GpuResourceManager(backend); var scope = manager.CreateScope();
        Inputs inputs = CreateInputs(scope); GpuBindingsRef bindings = scope.GetBindings(program, 0, inputs);
        using GpuResourcePin pin = manager.Pin(bindings);

        scope.Dispose(); manager.Trim();

        Assert.Empty(backend.Destroyed);
        Assert.NotNull(manager.GetBindingsHandle(bindings));
        pin.Dispose(); manager.Trim();
        Assert.Equal("Destroy:Bindings", backend.Events[0]);
        Assert.Equal(3, backend.Destroyed.Count);
    }

    [Fact]
    public void FailedBindingDestructionQuarantinesItsDependenciesWithoutRetry()
    {
        var backend = new ManagerTestBackend(); using var program = LoadProgram(backend);
        var manager = new GpuResourceManager(backend); var scope = manager.CreateScope();
        Inputs inputs = CreateInputs(scope); GpuBindingsRef bindings = scope.GetBindings(program, 0, inputs);
        backend.DestructionErrors.Add(manager.GetBindingsHandle(bindings), new InvalidOperationException("binding release unknown"));
        scope.Dispose();

        Assert.Throws<InvalidOperationException>(() => manager.Collect());
        Assert.Throws<InvalidOperationException>(() => manager.Collect());

        Assert.Single(backend.Destroyed);
        Assert.Equal(1, manager.Statistics.QuarantinedResourceCount);
        Assert.NotNull(manager.GetBufferRange(inputs.Buffer).Buffer);
        Assert.Throws<ObjectDisposedException>(() => manager.GetBindingsHandle(bindings));
    }

    [Fact]
    public async Task DifferentPreparedLayoutIdentitiesDoNotShareBindings()
    {
        var backend = new ManagerTestBackend(); using var firstProgram = LoadProgram(backend); using var secondProgram = LoadProgram(backend);
        await using var manager = new GpuResourceManager(backend); using var scope = manager.CreateScope(); Inputs inputs = CreateInputs(scope);

        GpuBindingsRef first = scope.GetBindings(firstProgram, 0, inputs);
        GpuBindingsRef second = scope.GetBindings(secondProgram, 0, inputs);

        Assert.NotSame(first, second);
        Assert.Equal(2, backend.CreatedBindings.Count);
    }

    [Fact]
    public async Task MissingInputsAreForwardedToBackendValidation()
    {
        var backend = new ManagerTestBackend(); using var program = LoadProgram(backend);
        await using var manager = new GpuResourceManager(backend); using var scope = manager.CreateScope();

        scope.GetBindings(program, 0, new EmptyInputs());

        Assert.Empty(Assert.Single(backend.CreatedBindings).Entries);
    }
    private sealed class EmptyInputs : IGpuBindingInputs { public void Write(GpuBindingWriter writer) { } }
}
