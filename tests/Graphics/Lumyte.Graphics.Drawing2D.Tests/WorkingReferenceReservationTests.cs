using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks conservative argument-generation charges before materialization.</summary>
public sealed class WorkingReferenceReservationTests
{
    /// <summary>One hundred twenty-eight independent groups reserve their later reference-generation charge.</summary>
    /// <returns>The asynchronous no-GPU materialization check.</returns>
    [Fact]
    public async Task CollectingGroupsCoverMaterializedGenerationBytesAsync()
    {
        var device = new ProfilingDevice();
        using var plan = new DrawingArgumentPlan2D(device);
        using IGraphicsBuffer<Vector4> buffer = device.CreateBuffer<Vector4>(new() { Count = 1, Usage = BufferUsage.ShaderRead });
        for (int i = 0; i < 128; i++)
        {
            ShaderResources scope = plan.Reserve();
            scope.RegisterBuffer(buffer.SliceElements(0, 1), _ => { });
            plan.ReserveWorkingReferences(scope, 2, 0, 0);
        }

        Assert.False(plan.Registrations.IsMaterialized);
        long collecting = plan.EstimatedBytes;
        Assert.Equal(256 + (128L * 4 * 256), collecting);
        await plan.SealAsync();
        Assert.True(plan.Registrations.IsMaterialized);
        Assert.NotEmpty(device.Tables);
        Assert.True(collecting >= plan.EstimatedBytes);
    }

    /// <summary>Replacement, rollback, cancellation and retirement return only their own reservations.</summary>
    /// <returns>The asynchronous collecting and retirement checks.</returns>
    [Fact]
    public async Task WorkingReferenceReservationsFollowScopeLifetimeAsync()
    {
        var device = new ProfilingDevice();
        using var plan = new DrawingArgumentPlan2D(device);
        ShaderResources first = plan.Reserve();
        plan.ReserveWorkingReferences(first, 2, 0, 0);
        long original = plan.EstimatedBytes;
        (int Scopes, int Uploads) checkpoint = plan.Checkpoint();
        ShaderResources second = plan.Reserve();
        plan.ReserveWorkingReferences(second, 8, 1, 1);
        Assert.True(plan.EstimatedBytes > original);
        plan.Rollback(checkpoint);
        Assert.Equal(original, plan.EstimatedBytes);
        Assert.Throws<InvalidOperationException>(() => plan.ReserveWorkingReferences(second, 1, 0, 0));
        plan.ReserveWorkingReferences(first, 1, 0, 0);
        Assert.Equal(256 + (3 * 256), plan.EstimatedBytes);
        plan.CancelUnpublished(first);
        Assert.Equal(256, plan.EstimatedBytes);
        ShaderResources last = plan.Reserve();
        plan.ReserveWorkingReferences(last, 3, 0, 0);
        await plan.SealAsync();
        plan.RetirePreparedResources([last]);
        Assert.Empty(plan.Reservations);
        Assert.Equal(256, plan.EstimatedBytes);
    }

    /// <summary>Invalid counts do not mutate prior reservations, and aggregate admission never wraps.</summary>
    [Fact]
    public void InvalidAndOverflowingChargesCannotPublishCapacity()
    {
        var device = new ProfilingDevice();
        using var plan = new DrawingArgumentPlan2D(device);
        ShaderResources scope = plan.Reserve();
        plan.ReserveWorkingReferences(scope, 2, 0, 0);
        long bytes = plan.EstimatedBytes;
        Assert.Throws<ArgumentOutOfRangeException>(() => plan.ReserveWorkingReferences(scope, 2, -1, 0));
        Assert.Equal(bytes, plan.EstimatedBytes);
        DrawingWorkingBudget2D.ValidateCumulative(long.MaxValue - 1, 1, long.MaxValue);
        Assert.Throws<NotSupportedException>(() => DrawingWorkingBudget2D.ValidateCumulative(long.MaxValue, 1, long.MaxValue));
    }
}
