using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;
using Xunit;
using EncoderValidation = Lumyte.Graphics.Vulkan.EncoderValidation;

namespace Lumyte.Graphics.Tests;

/// <summary>Exercises the same validation boundary called by native draw and dispatch recording.</summary>
public sealed class EncoderValidationTests
{
    /// <summary>Checks repeated dispatch accesses, rather than only binding-time lifetime retention.</summary>
    /// <param name="enabled">Whether graphics validation is enabled.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StorageWritesRequireAnExecutionDependency(bool enabled)
    {
        GraphicsValidation? validation = enabled ? new() : null;
        object command = new();
        var reference = new FakeReference(new(), ShaderBufferAccess.ReadWrite);
        IShaderReference[] references = [reference];
        EncoderValidation.ShaderAccesses(validation, command, references, PipelineStage.ComputeShader);
        void DispatchAgain() => EncoderValidation.ShaderAccesses(validation, command, references, PipelineStage.ComputeShader);
        if (enabled)
        {
            Assert.Throws<InvalidOperationException>(DispatchAgain);
        }
        else
        {
            DispatchAgain();
            Assert.Equal(0, reference.Probes);
        }
    }

    /// <summary>Checks that diagnostics never probe registrations when the per-device state is null.</summary>
    [Fact]
    public void DisabledValidationDoesNotProbeReferencesOrAllocate()
    {
        var reference = new FakeReference(new(), ShaderBufferAccess.ReadOnly) { Stale = true };
        IShaderReference[] references = [reference];
        object command = new();
        EncoderValidation.ShaderAccesses(null, command, references, PipelineStage.ComputeShader);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            EncoderValidation.ShaderAccesses(null, command, references, PipelineStage.ComputeShader);
        }

        Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
        Assert.Equal(0, reference.Probes);
    }

    /// <summary>Checks released registration identities at the actual execution access boundary.</summary>
    [Fact]
    public void ExecutionRejectsStaleRegistration()
    {
        var reference = new FakeReference(new(), ShaderBufferAccess.ReadOnly) { Stale = true };
        Assert.Throws<InvalidOperationException>(() => EncoderValidation.ShaderAccesses(new(), new(), [reference], PipelineStage.VertexShader | PipelineStage.FragmentShader));
    }

    /// <summary>Checks that sampled access uses exactly the selected mip and layer range.</summary>
    [Fact]
    public void SamplingUsesTheViewSubresources()
    {
        var texture = (IGraphicsTexture)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Lumyte.Graphics.Vulkan.VulkanDevice).Assembly.GetType("Lumyte.Graphics.Vulkan.VulkanTexture")!);
        var view = new FakeView(texture);
        var reference = new FakeReference(view, ShaderBufferAccess.ReadOnly);
        var validation = new GraphicsValidation();
        object command = new();
        var scope = new BarrierScope(PipelineStage.FragmentShader, ResourceAccess.ShaderRead);
        validation.TextureAccess(command, texture, new(0, 1, 0, 1), TextureState.CopyDestination, new(PipelineStage.Copy, ResourceAccess.CopyWrite));
        EncoderValidation.ShaderAccesses(validation, command, [reference], PipelineStage.FragmentShader);
        Assert.Throws<InvalidOperationException>(() => validation.TextureAccess(command, texture, new(2, 1, 3, 1), TextureState.CopyDestination, scope));
    }

    /// <summary>Checks copy-to-shader hazards through the physical backing allocation identity.</summary>
    [Fact]
    public void ShaderDataUsesTheBackingAllocation()
    {
        var data = new FakeStorage();
        var reference = new FakeReference(data, ShaderBufferAccess.ReadOnly);
        var validation = new GraphicsValidation();
        object command = new();
        validation.Access(command, data.ValidationStorage, new(PipelineStage.Copy, ResourceAccess.CopyWrite), 16, 16);
        Assert.Throws<InvalidOperationException>(() => EncoderValidation.ShaderAccesses(validation, command, [reference], PipelineStage.ComputeShader));
    }

    /// <summary>Registration callbacks remain live until submission rather than only being checked at capture.</summary>
    [Fact]
    public void SubmissionRevalidatesCapturedRegistration()
    {
        var validation = new GraphicsValidation();
        var command = (IGraphicsCommandBuffer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Lumyte.Graphics.Vulkan.VulkanDevice).Assembly.GetType("Lumyte.Graphics.Vulkan.VulkanCommandBuffer")!);
        var reference = new FakeReference(new(), ShaderBufferAccess.ReadOnly);
        EncoderValidation.ShaderAccesses(validation, command, [reference], PipelineStage.ComputeShader);
        reference.Stale = true;
        Assert.Throws<InvalidOperationException>(() => validation.PrepareSubmission([command]));
        Assert.Equal(2, reference.Probes);
        validation.Discard(command);
        validation.PrepareSubmission([command]);
        Assert.Equal(2, reference.Probes);
    }

    /// <summary>Per-execution tracking retains both table and resource lifetimes until discard.</summary>
    [Fact]
    public void ExecutionRetainsTableAndResource()
    {
        var validation = new GraphicsValidation();
        object command = new();
        var reference = new FakeReference(new(), ShaderBufferAccess.ReadOnly);
        EncoderValidation.ShaderAccesses(validation, command, [reference], PipelineStage.ComputeShader);
        Assert.Throws<InvalidOperationException>(() => validation.Release(reference.Table));
        Assert.Throws<InvalidOperationException>(() => validation.Release(reference.Resource));
        validation.Discard(command);
        validation.Release(reference.Table);
        validation.Release(reference.Resource);
    }

    /// <summary>Aliased bindings in one shader execution are not mistaken for consecutive dispatches.</summary>
    [Fact]
    public void AliasedRegistrationsAreCombinedWithinOneExecution()
    {
        object storage = new(), command = new();
        var validation = new GraphicsValidation();
        IShaderReference[] references = [new FakeReference(storage, ShaderBufferAccess.ReadOnly), new FakeReference(storage, ShaderBufferAccess.ReadWrite)];
        EncoderValidation.ShaderAccesses(validation, command, references, PipelineStage.ComputeShader);
        Assert.Throws<InvalidOperationException>(() => EncoderValidation.ShaderAccesses(validation, command, references, PipelineStage.ComputeShader));
    }

    /// <summary>Depth-only and stage-local roots do not acquire fictitious fragment-stage accesses.</summary>
    [Fact]
    public void RenderAccessStagesFollowRootDeclarations()
    {
        const string Root = """
            {"parameters":[{"name":"arguments","type":{"kind":"constantBuffer"}}]}
            """;
        const string Empty = """
            {"parameters":[]}
            """;
        var vertex = new ShaderTargetData(ShaderTarget.SpirV, ShaderStage.Vertex, "main", "test", "row-major", [], Root);
        var fragment = new ShaderTargetData(ShaderTarget.SpirV, ShaderStage.Fragment, "main", "test", "row-major", [], Empty);
        Assert.Equal(PipelineStage.VertexShader, EncoderValidation.RenderStages(vertex, null, "arguments"));
        Assert.Equal(PipelineStage.VertexShader, EncoderValidation.RenderStages(vertex, fragment, "arguments"));
        var fragmentRoot = new ShaderTargetData(ShaderTarget.SpirV, ShaderStage.Fragment, "main", "test", "row-major", [], Root);
        Assert.Equal(PipelineStage.VertexShader | PipelineStage.FragmentShader, EncoderValidation.RenderStages(vertex, fragmentRoot, "arguments"));
    }

    private sealed class FakeStorage : Lumyte.Graphics.Vulkan.IValidatedShaderDataStorage
    {
        public object ValidationStorage { get; } = new();
    }

    private sealed class FakeView(IGraphicsTexture texture) : IGraphicsTextureView
    {
        public IGraphicsTexture Texture => texture;

        public TextureViewInfo Info { get; } = new() { Format = TextureFormat.Rgba8Unorm, Dimension = TextureViewDimension.D2, BaseMipLevel = 2, MipLevelCount = 1, BaseArrayLayer = 3, ArrayLayerCount = 1 };

        public void Dispose()
        {
        }
    }

    private sealed class FakeReference(object resource, ShaderBufferAccess access) : IShaderReference, IValidatedShaderReference
    {
        public object Table { get; } = new();

        public object Resource => resource;

        public ShaderBufferAccess BufferAccess => access;

        public uint Slot => 0;

        public ulong OffsetInBytes => 16;

        public ulong SizeInBytes => 16;

        public ulong RegistrationOffsetInBytes => 16;

        public ulong RegistrationSizeInBytes => 16;

        public ulong Count => 1;

        public int Probes { get; private set; }

        public bool Stale { get; set; }

        public void ValidateRegistration()
        {
            Probes++;
            if (Stale)
            {
                throw new InvalidOperationException("Stale registration.");
            }
        }
    }
}
