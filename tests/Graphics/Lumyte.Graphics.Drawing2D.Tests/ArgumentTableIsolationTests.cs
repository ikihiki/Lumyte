using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks independent usage scopes do not inherit future sampled resources.</summary>
public sealed class ArgumentTableIsolationTests
{
    /// <summary>Disabling opportunistic sharing preserves every declared member in its own table.</summary>
    [Fact]
    public void IndependentGroupsKeepCompleteDistinctTables()
    {
        var device = new ProfilingDevice();
        using var plan = new ArgumentTablePlan(device);
        using var source = new DispatchTexture();
        using var intermediate = new DispatchTexture();
        using IGraphicsTextureView sourceView = source.CreateView();
        using IGraphicsTextureView intermediateView = intermediate.CreateView();
        ArgumentTableReservation first = plan.Reserve();
        ArgumentTableReservation second = plan.Reserve();
        IGpuRef<IGraphicsTextureView>? sourceReference = null;
        IGpuRef<IGraphicsTextureView>? intermediateReference = null;
        first.RegisterTexture(sourceView, reference => sourceReference = reference);
        second.RegisterTexture(intermediateView, reference => intermediateReference = reference);

        plan.Materialize(shareTables: false);

        Assert.Equal(2, device.Tables.Count);
        Assert.NotSame(first.Table, second.Table);
        Assert.Equal(1u, first.Table.TextureCapacity);
        Assert.Equal(1u, second.Table.TextureCapacity);
        Assert.Same(first.Table, ProfilingDevice.Owner(sourceReference!));
        Assert.Same(second.Table, ProfilingDevice.Owner(intermediateReference!));
    }
}
