using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;
using Xunit;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks external program ownership independently of native GPU creation.</summary>
public sealed class GraphicsProgramTests
{
    /// <summary>Checks an old revision stays retained while a new revision is acquired.</summary>
    [Fact]
    public void IndependentLeasesRetainTheirExactRevisions()
    {
        var device = new ProgramTestDevice();
        using GraphicsProgramLease owner = ImageEffectPrograms.DefaultProvider.Acquire(device, ImageEffectPrograms.ProgramId);
        using GraphicsProgramLease next = ImageEffectPrograms.DefaultProvider.Acquire(device, ImageEffectPrograms.ProgramId);
        int retained = 2;
        var old = new GraphicsProgramLease(owner.Resources, () => retained--);
        var current = new GraphicsProgramLease(next.Resources, () => retained--);
        Assert.NotSame(old.Resources.Pipeline, current.Resources.Pipeline);
        old.Dispose();
        old.Dispose();
        Assert.Equal(1, retained);
        Assert.Same(device, current.Resources.Device);
        Assert.Throws<ObjectDisposedException>(() => old.Resources);
        Assert.Empty(device.Released);
        current.Dispose();
        Assert.Equal(0, retained);
    }

    /// <summary>Checks program destruction follows native dependency order.</summary>
    [Fact]
    public void NativeProgramLeaseReleasesPipelineBeforeShaders()
    {
        var device = new ProgramTestDevice();
        GraphicsProgramLease lease = ImageEffectPrograms.DefaultProvider.Acquire(device, ImageEffectPrograms.ProgramId);
        Assert.Empty(device.Released);
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(["pipeline", "fragment", "vertex"], device.Released);
    }

    /// <summary>Checks partial native creation failures release every successful predecessor.</summary>
    /// <param name="failure">The native creation to fail.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PartialCreationFailureReturnsNativeChildren(int failure)
    {
        var device = new ProgramTestDevice { FailOnCreation = failure };
        Assert.Throws<InvalidOperationException>(() => ImageEffectPrograms.DefaultProvider.Acquire(device, ImageEffectPrograms.ProgramId));
        Assert.Equal(failure - 1, device.Released.Count);
        if (failure == 3)
        {
            Assert.Equal(["fragment", "vertex"], device.Released);
        }
    }

    /// <summary>Checks a foreign physical generation is rejected and its lease is returned on append failure.</summary>
    [Fact]
    public void EffectRejectsForeignDeviceAndReturnsProgramLease()
    {
        using GraphicsProgramLease native = ImageEffectPrograms.DefaultProvider.Acquire(new ProgramTestDevice(), ImageEffectPrograms.ProgramId);
        var programs = new BorrowedProvider(native.Resources);
        var graph = new Graph(new ProgramTestDevice());
        var image = new EffectImage(graph.CreateTexture(new() { Width = 1, Height = 1, Format = TextureFormat.Rgba16Float, Usage = TextureUsage.Sampled | TextureUsage.RenderAttachment }), new(0, 0, 1, 1), EffectColorSemantic.DisplayLinear);
        Assert.Throws<ArgumentException>(() => graph.AppendOpacity(image, 0.5f, programs));
        Assert.Equal(1, programs.Returned);
    }

    /// <summary>Checks program identifiers cannot silently select another embedded ABI.</summary>
    [Fact]
    public void UnknownProgramDoesNotFallBackToEmbeddedShaders()
    {
        var device = new ProgramTestDevice();
        Assert.Throws<NotSupportedException>(() => ImageEffectPrograms.DefaultProvider.Acquire(device, "unknown"));
        Assert.Empty(device.Released);
    }

    private sealed class BorrowedProvider(GraphicsProgramResources resources) : IGraphicsProgramProvider
    {
        internal int Returned { get; private set; }

        public GraphicsProgramLease Acquire(IGraphicDevice device, string programId)
            => new(resources, () => Returned++);
    }
}
