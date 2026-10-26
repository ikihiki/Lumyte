using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Checks external program ownership independently of native GPU creation.</summary>
public sealed class GraphicsProgramTests
{
    private const string ProgramId = "test.graphics-program.v1";
    private static readonly IGraphicsProgramProvider _programs = new EmbeddedGraphicsProgramProvider(typeof(ImageEffectShaders).Assembly, ProgramId, "Lumyte.ImageEffects.fullscreen.lshader", "Lumyte.ImageEffects.effects.lshader");

    /// <summary>Checks an old revision stays retained while a new revision is acquired.</summary>
    [Fact]
    public void IndependentLeasesRetainTheirExactRevisions()
    {
        var device = new ProgramTestDevice();
        using GraphicsProgramLease owner = _programs.Acquire(device, ProgramId);
        using GraphicsProgramLease next = _programs.Acquire(device, ProgramId);
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
        GraphicsProgramLease lease = _programs.Acquire(device, ProgramId);
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
        Assert.Throws<InvalidOperationException>(() => _programs.Acquire(device, ProgramId));
        Assert.Equal(failure - 1, device.Released.Count);
        if (failure == 3)
        {
            Assert.Equal(["fragment", "vertex"], device.Released);
        }
    }

    /// <summary>Checks an external owner can reject a resource after runtime invalidation.</summary>
    [Fact]
    public void LeaseValidatesRuntimeBeforeReturningResources()
    {
        using GraphicsProgramLease native = _programs.Acquire(new ProgramTestDevice(), ProgramId);
        bool invalid = false;
        int returned = 0;
        using var lease = new GraphicsProgramLease(native.Resources, () => returned++, () =>
        {
            if (invalid)
            {
                throw new InvalidOperationException("The physical generation was lost.");
            }
        });
        Assert.Same(native.Resources, lease.Resources);
        invalid = true;
        Assert.Throws<InvalidOperationException>(() => lease.Resources);
        lease.Dispose();
        Assert.Equal(1, returned);
    }

    /// <summary>Checks program identifiers cannot silently select another embedded ABI.</summary>
    [Fact]
    public void UnknownProgramDoesNotFallBackToEmbeddedShaders()
    {
        var device = new ProgramTestDevice();
        Assert.Throws<NotSupportedException>(() => _programs.Acquire(device, "unknown"));
        Assert.Empty(device.Released);
    }
}
