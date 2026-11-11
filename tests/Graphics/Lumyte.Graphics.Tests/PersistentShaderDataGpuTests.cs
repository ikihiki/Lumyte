using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.RenderGraph;
using Lumyte.Graphics.Samples;
using Lumyte.Graphics.Shared;
using Lumyte.Graphics.Vulkan;
using Lumyte.Graphics.Wgpu;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks persistent uploads through real backend allocations and public APIs.</summary>
public sealed class PersistentShaderDataGpuTests
{
    /// <summary>Checks mapped native uploads, provenance and immutable partial snapshots.</summary>
    /// <returns>The device verification.</returns>
    [GpuFact]
    public async Task VulkanPersistentPublicApiAsync()
    {
        using var owner = VulkanDevice.Create();
        using var foreign = VulkanDevice.Create();
        await CheckAsync(owner, foreign);
    }

    /// <summary>Checks the portable backend uses the same storage API without pretending to use addresses.</summary>
    /// <returns>The device verification.</returns>
    [GpuFact]
    public async Task WgpuPersistentPublicApiAsync()
    {
        using var owner = WgpuDevice.Create();
        using var foreign = WgpuDevice.Create();
        await CheckAsync(owner, foreign);
    }

    private static ShaderArtifact Artifact(string name) => ShaderArtifact.LoadEmbedded(typeof(ShaderBindingExercise).Assembly, "Lumyte.Shaders." + name + ".lshader");

    private static async Task CheckAsync(IGraphicDevice owner, IGraphicDevice foreign)
    {
        ShaderArtifact artifact = Artifact("binding-fragment");
        using IGraphicsShaderDataBuffer<BindingMaterial> staging = owner.CreateShaderDataBuffer<BindingMaterial>(artifact, 3, MemoryPreference.Upload);
        using IGraphicsShaderDataBuffer<BindingMaterial> other = owner.CreateShaderDataBuffer<BindingMaterial>(artifact, 3, MemoryPreference.Upload);
        using IGraphicsShaderDataBuffer<BindingMaterial> gpu = owner.CreateShaderDataBuffer<BindingMaterial>(artifact, 3);
        using IGraphicsShaderDataBuffer<BindingMaterial> outside = foreign.CreateShaderDataBuffer<BindingMaterial>(artifact, 3, MemoryPreference.Upload);
        using IGraphicsTexture texture = owner.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.Sampled });
        using IGraphicsTextureView view = texture.CreateView();
        using IGraphicsSampler sampler = owner.CreateSampler(new());
        using IGraphicsSampler foreignSampler = foreign.CreateSampler(new());
        using IArgumentTable table = owner.CreateArgumentTable(new() { TextureCapacity = 1, SamplerCapacity = 1 });
        using IArgumentTable secondTable = owner.CreateArgumentTable(new() { TextureCapacity = 1, SamplerCapacity = 1 });
        using IArgumentTable foreignTable = foreign.CreateArgumentTable(new() { SamplerCapacity = 1 });
        IGpuRef<IGraphicsTextureView> textureRef = table.WriteTexture(0, view);
        IGpuRef<IGraphicsSampler> samplerRef = table.WriteSampler(0, sampler);
        IGpuRef<IGraphicsSampler> sameSlot = secondTable.WriteSampler(0, sampler);
        IGpuRef<IGraphicsSampler> foreignRef = foreignTable.WriteSampler(0, foreignSampler);
        BindingMaterial[] initial = [new(Vector4.One, textureRef, samplerRef), new(new(2), textureRef, samplerRef)];
        ShaderDataStorage<BindingMaterial> source = staging.CreateStorage(initial);
        ShaderDataStorage<BindingMaterial> empty = staging.CreateStorage([]);
        var publicLayout = ShaderDataLayout.Data(artifact, owner.Caps.ShaderTarget, "BindingMaterial");
        ShaderDataStorage<BindingMaterial> forged = publicLayout.CreateStorage<BindingMaterial>(owner, [initial[0]], static (_, _) => Enumerable.Repeat((byte)255, 16).ToArray());
        ShaderDataStorage<BindingMaterial> forgedEmpty = publicLayout.CreateStorage<BindingMaterial>(owner, [], static (_, _) => new byte[16]);
        initial[0] = default;
        Assert.Equal(2, source.Count);
        Assert.Equal(0, empty.Count);
        Assert.Throws<InvalidOperationException>(() => staging.CopyFrom(source));
        Assert.Throws<InvalidOperationException>(() => gpu.CopyFrom(source));
        Assert.Throws<InvalidOperationException>(() => ((IShaderDataSource)staging).Read(0));
        Assert.Throws<ArgumentException>(() => staging.CreateStorage([new(Vector4.One, textureRef, foreignRef)]));
        Assert.Throws<ArgumentException>(() => source.SetReference(0, BindingMaterial.StorageMembers.Sampler, foreignRef));
        Assert.Throws<ArgumentException>(() => source.SetReference(0, BindingMaterial.StorageMembers.Sampler, new FakeReference()));
        Assert.Throws<ArgumentException>(() => source.Set(0, new(Vector4.Zero, textureRef, foreignRef)));
        Assert.Throws<ArgumentException>(() => source.SetValue(0, new ShaderDataValueMember<BindingMaterial, float>("Color"), 1f));
        Assert.Throws<ArgumentException>(() => source.SetValue(0, default(ShaderDataValueMember<BindingMaterial, Vector4>), Vector4.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.SetValue(-1, BindingMaterial.StorageMembers.Color, Vector4.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => source.SetValue(2, BindingMaterial.StorageMembers.Color, Vector4.Zero));
        await staging.MapAsync();
        Assert.Throws<ArgumentException>(() => staging.CopyFrom(forged));
        Assert.Throws<ArgumentException>(() => staging.CopyFrom(forgedEmpty));
        staging.CopyFrom(source, elementOffset: 1);
        ShaderValueSnapshot old = ((IShaderDataSource)staging).Read(1);
        Assert.Equal(Vector4.One, ReadColor(old));
        Assert.True(MemoryMarshal.TryGetArray(old.Values[0].Data, out ArraySegment<byte> logicalBytes));
        Assert.NotNull(logicalBytes.Array);
        Assert.All(logicalBytes.Array.AsSpan(16, 32).ToArray(), dataByte => Assert.Equal((byte)0, dataByte));
        Assert.Same(samplerRef, old.Values[2].Reference);
        Assert.Throws<InvalidOperationException>(() => ((IShaderDataSource)staging).Read(0));
        source.SetValue(0, BindingMaterial.StorageMembers.Color, new Vector4(3));
        source.SetReference(0, BindingMaterial.StorageMembers.Sampler, sameSlot);
        staging.CopyFrom(source, 0, 1, 2);
        Assert.Equal(new Vector4(3), ReadColor(((IShaderDataSource)staging).Read(2)));
        Assert.Same(sameSlot, ((IShaderDataSource)staging).Read(2).Values[2].Reference);
        Assert.Equal(Vector4.One, ReadColor(old));
        Assert.Same(samplerRef, old.Values[2].Reference);
        Assert.Equal(Vector4.One, ReadColor(((IShaderDataSource)staging).Read(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => staging.CopyFrom(source, int.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => staging.CopyFrom(source, 0, -2));
        Assert.Throws<ArgumentOutOfRangeException>(() => staging.CopyFrom(source, 0, 1, ulong.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() => staging.CopyFrom(source, 0, 2, 2));
        staging.CopyFrom(source, 2, 0, 3);
        staging.CopyFrom(empty, 0, 0, 3);
        Assert.Equal(Vector4.One, ReadColor(old));
        Assert.Same(old, ((IShaderDataSource)staging).Read(1));
        staging.Unmap();
        using (IGraphicsCommandBuffer abandoned = owner.CreateCommandBuffer(new()))
        {
            abandoned.CopyBuffer(staging.SliceElements(1, 1), gpu.SliceElements(0, 1));
            Assert.Throws<InvalidOperationException>(() => ((IShaderDataSource)gpu).Read(0));
            Assert.Throws<InvalidOperationException>(() => owner.Queue.Submit([abandoned]));
            Assert.Throws<InvalidOperationException>(() => ((IShaderDataSource)gpu).Read(0));
        }

        Assert.Throws<InvalidOperationException>(() => ((IShaderDataSource)gpu).Read(0));
        using (var graph = new Lumyte.Graphics.RenderGraph.RenderGraph(owner))
        {
            RenderGraphShaderDataBuffer<BindingMaterial> graphSource = graph.ImportShaderDataBuffer(staging, new(PipelineStage.Host, ResourceAccess.HostWrite));
            RenderGraphShaderDataBuffer<BindingMaterial> graphDestination = graph.ImportShaderDataBuffer(gpu, default);
            graph.AddPass("persistent-partial-copy", context =>
            {
                IGraphicsShaderDataBuffer<BindingMaterial> resolvedSource = context.GetShaderDataBuffer(graphSource);
                Assert.Same(staging, resolvedSource);
                context.Commands.CopyBuffer(resolvedSource.SliceElements(1, 1), context.GetShaderDataBuffer(graphDestination).SliceElements(0, 1));
            })
                .UseShaderDataBuffer(graphSource, new(PipelineStage.Copy, ResourceAccess.CopyRead))
                .UseShaderDataBuffer(graphDestination, new(PipelineStage.Copy, ResourceAccess.CopyWrite)).KeepAlive();
            using CompiledRenderGraph plan = graph.Compile();
            using IGraphicsCommandBuffer graphCommands = owner.CreateCommandBuffer(new());
            plan.Record(graphCommands);
            Assert.Throws<InvalidOperationException>(() => ((IShaderDataSource)gpu).Read(0));
            graphCommands.Finish();
            using IGraphicsSubmission graphSubmission = owner.Queue.Submit([graphCommands]);
            Assert.Same(old, ((IShaderDataSource)gpu).Read(0));
            Assert.Throws<InvalidOperationException>(() => ((IShaderDataSource)gpu).Read(1));
            await graphSubmission.WaitAsync();
        }

        await other.MapAsync();
        other.CopyFrom(source);
        other.Unmap();
        await outside.MapAsync();
        Assert.Throws<ArgumentException>(() => outside.CopyFrom(empty, 0, 0));
        outside.Unmap();
        staging.Dispose();
        Assert.Throws<ObjectDisposedException>(() => staging.CreateStorage([]));
        Assert.Equal(Vector4.One, ReadColor(old));
        Assert.Same(samplerRef, old.Values[2].Reference);
    }

    private static Vector4 ReadColor(ShaderValueSnapshot snapshot)
    {
        ReadOnlySpan<byte> bytes = snapshot.Values[0].Data.Span;
        return new(BinaryPrimitives.ReadSingleLittleEndian(bytes), BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[12..]));
    }

    private sealed class FakeReference : IGpuRef<IGraphicsSampler>
    {
        public ulong Count => 1;

        public IGpuRef<IGraphicsSampler> GetElement(ulong index) => this;
    }
}
