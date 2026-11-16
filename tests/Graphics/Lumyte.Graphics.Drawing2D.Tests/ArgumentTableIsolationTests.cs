using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks independent usage scopes do not inherit future sampled resources.</summary>
public sealed class ArgumentTableIsolationTests
{
    /// <summary>Device hints select the default while explicit modes override the hint.</summary>
    /// <param name="target">The backend shader target.</param>
    /// <param name="hint">The resolved device preference.</param>
    /// <param name="mode">The explicit selection policy.</param>
    /// <param name="tables">The expected physical table count.</param>
    /// <returns>The completed resource preparation.</returns>
    [Theory]
    [InlineData(ShaderTarget.Wgsl, true, DrawingArgumentTableMode.Auto, 2)]
    [InlineData(ShaderTarget.Wgsl, false, DrawingArgumentTableMode.Auto, 1)]
    [InlineData(ShaderTarget.Wgsl, true, DrawingArgumentTableMode.Shared, 1)]
    [InlineData(ShaderTarget.Wgsl, false, DrawingArgumentTableMode.DrawLocal, 2)]
    [InlineData(ShaderTarget.SpirV, false, DrawingArgumentTableMode.Auto, 1)]
    [InlineData(ShaderTarget.Dxil, false, DrawingArgumentTableMode.Auto, 1)]
    public async Task DrawingPlansRespectHintAndExplicitModeAsync(ShaderTarget target, bool hint, DrawingArgumentTableMode mode, int tables)
    {
        var device = new ProfilingDevice { Caps = new() { ShaderTarget = target, PreferDrawLocalArgumentTables = hint, MaxBufferSize = 1UL << 30 } };
        using var plan = new DrawingArgumentPlan2D(device) { ArgumentTableMode = mode };
        using IGraphicsBuffer<uint> first = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.ShaderRead });
        using IGraphicsBuffer<uint> second = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.ShaderRead });
        ShaderResources a = plan.Reserve();
        ShaderResources b = plan.Reserve();
        IGpuRef<uint>? firstReference = null;
        IGpuRef<uint>? duplicate = null;
        IGpuRef<uint>? secondReference = null;
        a.RegisterBuffer(first.SliceElements(0, 4), reference => firstReference = reference);
        a.RegisterBuffer(first.SliceElements(0, 4), reference => duplicate = reference);
        b.RegisterBuffer(second.SliceElements(0, 4), reference => secondReference = reference);

        await plan.SealAsync();

        Assert.Equal(tables, device.Tables.Count);
        Assert.Same(firstReference, duplicate);
        Assert.Equal(tables == 2, !ReferenceEquals(ProfilingDevice.Owner(firstReference!), ProfilingDevice.Owner(secondReference!)));
        Assert.All(device.Tables, table => Assert.Equal(tables == 2 ? 1u : 2u, table.BufferCapacity));
    }

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
