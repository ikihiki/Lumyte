using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>CPU checks for whole-draw partition selection and physical binding budgets.</summary>
public sealed class ArgumentPartitionTests
{
    /// <summary>The exact cap fits; the next draw rolls over without splitting its resources.</summary>
    /// <param name="draws">The number of complete draw reservations.</param>
    /// <param name="tables">The expected number of native partitions.</param>
    [Theory]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    public void ExactCapAndOneMorePartitionWholeDraws(int draws, int tables)
    {
        ProfilingDevice device = Device(new() { BufferSlots = 4 });
        using var plan = new ArgumentTablePlan(device);
        using IGraphicsBuffer<uint> buffer = device.CreateBuffer<uint>(new() { Count = 16, Usage = BufferUsage.ShaderRead });
        var scopes = new List<ArgumentTableReservation>();
        var references = new List<(ArgumentRegistration<uint> Root, ArgumentRegistration<uint> Nested)>();
        for (int i = 0; i < draws; i++)
        {
            ArgumentTableReservation scope = plan.Reserve();
            scopes.Add(scope);
            references.Add((scope.RegisterBuffer(buffer.SliceElements((ulong)i * 2, 1)), scope.RegisterBuffer(buffer.SliceElements(((ulong)i * 2) + 1, 1))));
        }

        Assert.Empty(device.Tables);
        plan.Materialize();
        Assert.Equal(tables, device.Tables.Count);
        Assert.Equal(4u, device.Tables[0].BufferCapacity);
        if (tables == 2)
        {
            Assert.Equal(2u, device.Tables[1].BufferCapacity);
            Assert.NotSame(scopes[1].Table, scopes[2].Table);
        }

        for (int i = 0; i < draws; i++)
        {
            Assert.Same(scopes[i].Table, ProfilingDevice.Owner(references[i].Root.Reference));
            Assert.Same(scopes[i].Table, ProfilingDevice.Owner(references[i].Nested.Reference));
        }
    }

    /// <summary>Aliases share a native reference only inside the selected partition.</summary>
    [Fact]
    public void SameResourceAliasesWithinPartitionButRebindsAfterRollover()
    {
        ProfilingDevice device = Device(new() { TextureSlots = 2 });
        using var plan = new ArgumentTablePlan(device);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView shared = texture.CreateView();
        using IGraphicsTextureView other = texture.CreateView();
        using IGraphicsTextureView third = texture.CreateView();
        ArgumentTableReservation a = plan.Reserve();
        ArgumentRegistration<IGraphicsTextureView> first = a.RegisterTexture(shared);
        ArgumentTableReservation b = plan.Reserve();
        ArgumentRegistration<IGraphicsTextureView> alias = b.RegisterTexture(shared);
        b.RegisterTexture(other);
        ArgumentTableReservation c = plan.Reserve();
        ArgumentRegistration<IGraphicsTextureView> rebound = c.RegisterTexture(shared);
        c.RegisterTexture(third);
        plan.Materialize();
        Assert.Equal(2, device.Tables.Count);
        Assert.Same(first.Reference, alias.Reference);
        Assert.NotSame(first.Reference, rebound.Reference);
        Assert.Same(a.Table, b.Table);
        Assert.NotSame(a.Table, c.Table);
        Assert.Same(c.Table, ProfilingDevice.Owner(rebound.Reference));
    }

    /// <summary>Each resource namespace has an independent slot budget.</summary>
    /// <param name="constrainedKind">The boundary test input.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void IndependentKindCapsDoNotConsumeOtherNamespaces(int constrainedKind)
    {
        ProfilingDevice device = Device(new() { TextureSlots = constrainedKind == 0 ? 1u : 2u, SamplerSlots = constrainedKind == 1 ? 1u : 2u, BufferSlots = constrainedKind == 2 ? 1u : 2u });
        using var plan = new ArgumentTablePlan(device);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView firstView = texture.CreateView();
        using IGraphicsTextureView secondView = texture.CreateView();
        using var firstSampler = new DispatchSampler();
        using var secondSampler = new DispatchSampler();
        using IGraphicsBuffer<uint> buffer = device.CreateBuffer<uint>(new() { Count = 2, Usage = BufferUsage.ShaderRead });
        ArgumentTableReservation first = plan.Reserve();
        first.RegisterTexture(firstView);
        first.RegisterSampler(firstSampler);
        first.RegisterBuffer(buffer.SliceElements(0, 1));
        ArgumentTableReservation second = plan.Reserve();
        second.RegisterTexture(secondView);
        second.RegisterSampler(secondSampler);
        second.RegisterBuffer(buffer.SliceElements(1, 1));
        plan.Materialize();
        Assert.Equal(2, device.Tables.Count);
        Assert.All(device.Tables, desc => Assert.Equal(new ArgumentTableDesc { TextureCapacity = 1, SamplerCapacity = 1, BufferCapacity = 1 }, desc));
    }

    /// <summary>Compact WGSL remapping allows a logical table larger than one draw's binding count.</summary>
    [Fact]
    public void WgslLogicalTableGrowsUntilRemapSlotCap()
    {
        ProfilingDevice device = Device(new() { TextureSlots = 3, TexturesPerDraw = 1, DirectSlotBindings = false }, ShaderTarget.Wgsl);
        using var plan = new ArgumentTablePlan(device);
        using var texture = new DispatchTexture();
        IGraphicsTextureView[] views = Enumerable.Range(0, 4).Select(_ => texture.CreateView()).ToArray();
        try
        {
            ArgumentTableReservation[] scopes = views.Select(view =>
            {
                ArgumentTableReservation scope = plan.Reserve();
                scope.RegisterTexture(view);
                return scope;
            }).ToArray();
            plan.Materialize();
            Assert.Equal(new uint[] { 3, 1 }, device.Tables.Select(desc => desc.TextureCapacity));
            Assert.Same(scopes[0].Table, scopes[2].Table);
            Assert.NotSame(scopes[2].Table, scopes[3].Table);
        }
        finally
        {
            foreach (IGraphicsTextureView view in views)
            {
                view.Dispose();
            }
        }
    }

    /// <summary>All draw checks finish before allocating even the earlier valid table.</summary>
    /// <param name="reason">The boundary test input.</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void OversizeCombinedReservedAndLayoutFloorsFailBeforeNativeCreation(int reason)
    {
        ArgumentTableBindingLimits limits = reason switch
        {
            0 => new() { TextureSlots = 1 },
            1 => new() { CombinedBindings = 2, ColorAttachmentBindingWeight = 1 },
            2 => new() { CombinedBindings = 3, ReservedBindings = 2 },
            _ => new() { MinimumSamplerBindings = 2, SamplersPerDraw = 1 },
        };
        ProfilingDevice device = Device(limits);
        using var plan = new ArgumentTablePlan(device);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView first = texture.CreateView();
        using IGraphicsTextureView second = texture.CreateView();
        ArgumentTableReservation earlier = plan.Reserve();
        ArgumentRegistration<IGraphicsTextureView> pending = earlier.RegisterTexture(first);
        ArgumentTableReservation oversized = plan.Reserve();
        oversized.RegisterTexture(first);
        oversized.RegisterTexture(second);
        Assert.Throws<NotSupportedException>(() => plan.Materialize());
        Assert.Empty(device.Tables);
        Assert.Equal(0, device.Writes);
        Assert.Throws<InvalidOperationException>(() => pending.Reference);
    }

    /// <summary>Exact typed ranges retain identity while read/write consumes both descriptor uses.</summary>
    /// <param name="budget">The available storage descriptor count.</param>
    /// <param name="fits">Whether both descriptor accesses fit.</param>
    [Theory]
    [InlineData(1u, false)]
    [InlineData(2u, true)]
    public void ReadWriteRangeConsumesTwoBindings(uint budget, bool fits)
    {
        ProfilingDevice device = Device(new() { BuffersPerDraw = budget });
        using var plan = new ArgumentTablePlan(device);
        using IGraphicsBuffer<uint> buffer = device.CreateBuffer<uint>(new() { Count = 4, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite });
        ArgumentTableReservation scope = plan.Reserve();
        ArgumentRegistration<uint> range = scope.RegisterBuffer(buffer.SliceElements(0, 2), ArgumentBufferAccess.ReadWrite);
        Assert.Same(range, scope.RegisterBuffer(buffer.SliceElements(0, 2), ArgumentBufferAccess.ReadWrite));
        if (!fits)
        {
            Assert.Throws<NotSupportedException>(() => plan.Materialize());
            Assert.Empty(device.Tables);
            return;
        }

        plan.Materialize();
        Assert.Equal(1u, Assert.Single(device.Tables).BufferCapacity);
        Assert.Equal(2ul, range.Reference.Count);
    }

    /// <summary>Different exact ranges and access kinds never alias logical references.</summary>
    [Fact]
    public void RangeOffsetCountAndAccessRemainDistinct()
    {
        ProfilingDevice device = Device(new());
        using var plan = new ArgumentTablePlan(device);
        using IGraphicsBuffer<Vector4> buffer = device.CreateBuffer<Vector4>(new() { Count = 8, Usage = BufferUsage.ShaderRead | BufferUsage.ShaderWrite });
        ArgumentTableReservation scope = plan.Reserve();
        ArgumentRegistration<Vector4> read = scope.RegisterBuffer(buffer.SliceElements(0, 4));
        ArgumentRegistration<Vector4> offset = scope.RegisterBuffer(buffer.SliceElements(1, 4));
        ArgumentRegistration<Vector4> count = scope.RegisterBuffer(buffer.SliceElements(0, 3));
        ArgumentRegistration<Vector4> write = scope.RegisterBuffer(buffer.SliceElements(0, 4), ArgumentBufferAccess.Write);
        plan.Materialize();
        Assert.Equal(4u, Assert.Single(device.Tables).BufferCapacity);
        Assert.NotSame(read.Reference, offset.Reference);
        Assert.NotSame(read.Reference, count.Reference);
        Assert.NotSame(read.Reference, write.Reference);
    }

    /// <summary>Finite native storage ranges reject excessive byte sizes and unaligned offsets before allocation.</summary>
    /// <param name="unaligned">The boundary test input.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StorageRangeLimitsAreCheckedBeforeNativeTables(bool unaligned)
    {
        ProfilingDevice device = Device(new() { MaximumBufferRangeBytes = 8, BufferRangeAlignment = 8 });
        using var plan = new ArgumentTablePlan(device);
        using IGraphicsBuffer<uint> buffer = device.CreateBuffer<uint>(new() { Count = 8, Usage = BufferUsage.ShaderRead });
        plan.Reserve().RegisterBuffer(buffer.SliceElements(unaligned ? 1ul : 0ul, unaligned ? 1ul : 3ul));
        Assert.Throws<NotSupportedException>(() => plan.Materialize());
        Assert.Empty(device.Tables);
    }

    /// <summary>Direct-slot layouts partition at the reachable slot span, even when each draw uses one resource.</summary>
    [Fact]
    public void DirectSlotBindingsPartitionAtPerDrawReachableSpan()
    {
        ProfilingDevice device = Device(new() { TextureSlots = 8, TexturesPerDraw = 2, DirectSlotBindings = true });
        using var plan = new ArgumentTablePlan(device);
        using var texture = new DispatchTexture();
        using IGraphicsTextureView a = texture.CreateView();
        using IGraphicsTextureView b = texture.CreateView();
        using IGraphicsTextureView c = texture.CreateView();
        plan.Reserve().RegisterTexture(a);
        plan.Reserve().RegisterTexture(b);
        plan.Reserve().RegisterTexture(c);
        plan.Materialize();
        Assert.Equal(new uint[] { 2, 1 }, device.Tables.Select(desc => desc.TextureCapacity));
    }

    /// <summary>Selective module rollback preserves intervening scopes; checkpoint rollback preserves earlier modules.</summary>
    [Fact]
    public void DiscardAndRollbackPreserveOtherModuleScopes()
    {
        ProfilingDevice device = Device(new());
        using var plan = new ArgumentTablePlan(device);
        using var sampler = new DispatchSampler();
        ArgumentTableReservation abandoned = plan.Reserve();
        ArgumentRegistration<IGraphicsSampler> abandonedHandle = abandoned.RegisterSampler(sampler);
        ArgumentTableReservation otherModule = plan.Reserve();
        ArgumentRegistration<IGraphicsSampler> survivor = otherModule.RegisterSampler(sampler);
        plan.Discard([abandoned]);
        ArgumentTableCheckpoint checkpoint = plan.Checkpoint();
        ArgumentTableReservation speculative = plan.Reserve();
        ArgumentRegistration<IGraphicsSampler> rolledBack = speculative.RegisterSampler(sampler);
        plan.Rollback(checkpoint);
        Assert.Same(otherModule, Assert.Single(plan.Reservations));
        plan.Materialize();
        Assert.Single(device.Tables);
        Assert.Equal(1, device.Writes);
        Assert.Same(otherModule.Table, ProfilingDevice.Owner(survivor.Reference));
        Assert.Throws<InvalidOperationException>(() => abandonedHandle.Reference);
        Assert.Throws<InvalidOperationException>(() => rolledBack.Reference);
    }

    /// <summary>Real renderer roots and nested payload references follow their selected table across rollovers.</summary>
    /// <returns>The asynchronous test completion.</returns>
    [Fact]
    public async Task RendererRootsAndNestedReferencesUseSameOwnerAcrossPartitionsAsync()
    {
        ProfilingDevice device = Device(new() { BufferSlots = 2 });
        using var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var target = new DispatchTexture();
        using IGraphicsTextureView view = target.CreateView();
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 3; i++)
        {
            canvas.Save();
            canvas.ClipRect(new(0, 0, 16, 16));
            canvas.FillCircle(new(8), 3, new Color2D(1, 0, 0, 1));
            canvas.Restore();
        }

        using PreparedDraw2D prepared = await renderer.PrepareAsync(canvas.End(), new() { View = view });
        using var commands = new ProfilingCommands();
        prepared.Record(commands);
        DrawingArguments[] roots = commands.Events.OfType<DrawingArguments>().ToArray();
        IArgumentTable[] tables = commands.Events.OfType<IArgumentTable>().ToArray();
        Assert.Equal(3, device.Tables.Count);
        Assert.Equal(3, roots.Length);
        Assert.Equal(3, tables.Distinct().Count());
        Assert.Equal(roots.Sum(root => (int)root.Count), device.Instructions.Count);
        int instructionOffset = 0;
        for (int i = 0; i < roots.Length; i++)
        {
            Assert.Same(tables[i], ProfilingDevice.Owner(roots[i].Instructions));
            Assert.Same(tables[i], ProfilingDevice.Owner(roots[i].Data));
            foreach (DrawingInstruction instruction in device.Instructions.Skip(instructionOffset).Take((int)roots[i].Count))
            {
                Assert.Same(tables[i], ProfilingDevice.Owner(instruction.Texture));
                Assert.Same(tables[i], ProfilingDevice.Owner(instruction.Sampler));
            }

            instructionOffset += (int)roots[i].Count;
        }
    }

    /// <summary>All storage sizes are checked before even an earlier valid reservation creates a table.</summary>
    [Fact]
    public void MisSizedStorageRangeRejectsEntirePlanBeforeWrites()
    {
        ProfilingDevice device = Device(new() { BufferRangeSizeAlignment = 4 }, ShaderTarget.Wgsl);
        using IGraphicsBuffer<byte> buffer = device.CreateBuffer<byte>(new() { Count = 4, Usage = BufferUsage.ShaderRead });
        using var plan = new ArgumentTablePlan(device);
        ArgumentRegistration<byte> earlier = plan.Reserve().RegisterBuffer(buffer.SliceElements(0, 4));
        plan.Reserve().RegisterBuffer(buffer.SliceElements(0, 1));
        Assert.Throws<NotSupportedException>(() => plan.Materialize());
        Assert.Empty(device.Tables);
        Assert.Equal(0, device.Writes);
        Assert.Throws<InvalidOperationException>(() => earlier.Reference);
    }

    private static ProfilingDevice Device(ArgumentTableBindingLimits limits, ShaderTarget target = ShaderTarget.SpirV) => new()
    {
        Caps = new() { ShaderTarget = target, MaxBufferSize = 1UL << 30, ArgumentTableBindingLimits = limits },
    };
}
