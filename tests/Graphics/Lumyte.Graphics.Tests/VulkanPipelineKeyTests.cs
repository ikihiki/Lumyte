using System.Runtime.CompilerServices;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Tests;

/// <summary>Deterministic compact Vulkan cache identity and admission contracts.</summary>
public sealed class VulkanPipelineKeyTests
{
    private static readonly DeviceCaps _caps = new() { MaxColorAttachments = 8, Features = GraphicsFeatures.DepthBiasClamp };

    /// <summary>Checks KeysAreUnmanagedAndBounded.</summary>
    [Fact]
    public void KeysAreUnmanagedAndBounded()
    {
        Assert.Equal(64, Size<RenderStateKey>());
        Assert.True(Size<PipelineVariantKey>() <= 80);
        Assert.Equal(8, Size<DescriptorLayoutKey>());
        for (int n = 0; n <= 8; n++)
        {
            var state = GraphicsRenderStateDesc.ForColorTargets(n);
            RenderStateKey key = PipelineValidation.State(state, _caps).Key;
            Assert.Equal(key, PipelineValidation.State(GraphicsRenderStateDesc.ForColorTargets(n), _caps).Key);
            Assert.Equal(key.GetHashCode(), new RenderStateKey(state).GetHashCode());
            if (n > 0)
            {
                Assert.NotEqual(key, new RenderStateKey(GraphicsRenderStateDesc.ForColorTargets(n - 1)));
            }
        }

        Assert.Throws<NotSupportedException>(() => PipelineValidation.State(GraphicsRenderStateDesc.ForColorTargets(9), _caps));
        Assert.Throws<NotSupportedException>(() => PipelineValidation.State(GraphicsRenderStateDesc.ForColorTargets(2), _caps with { MaxColorAttachments = 1 }));
        Assert.Throws<NotSupportedException>(() => PipelineVariantKey.Create(default, new TextureFormat[9], null, default));
    }

    /// <summary>Checks EveryDescriptorFieldIncludingInactiveValuesChangesIdentity.</summary>
    [Fact]
    public void EveryDescriptorFieldIncludingInactiveValuesChangesIdentity()
    {
        var baseline = GraphicsRenderStateDesc.ForColorTargets(1);
        foreach (GraphicsRenderStateDesc changed in Variants(baseline))
        {
            RenderStateKey original = new(baseline);
            RenderStateKey key = new(changed);
            Assert.NotEqual(original, key);
            Assert.Equal(key, new RenderStateKey(changed));
            Assert.Equal(key.GetHashCode(), new RenderStateKey(changed).GetHashCode());
        }
    }

    /// <summary>Checks SnapshotIsIndependentAndOrderIsSignificant.</summary>
    [Fact]
    public void SnapshotIsIndependentAndOrderIsSignificant()
    {
        var colors = new List<ColorBlendStateDesc> { new(), new() { BlendEnable = true } };
        RenderStateSnapshot snapshot = PipelineValidation.State(new() { ColorTargets = colors }, _caps);
        RenderStateKey original = snapshot.Key;
        colors.Reverse();
        Assert.NotEqual(original, new RenderStateKey(new() { ColorTargets = colors }));
        colors.Clear();
        Assert.Equal(2, snapshot.Desc.ColorTargets.Count);
        Assert.False(snapshot.Desc.ColorTargets[0].BlendEnable);
        Assert.Equal(original, new RenderStateKey(snapshot.Desc));
        var dictionary = new Dictionary<RenderStateKey, int>(new CollisionComparer());
        dictionary.Add(original, 1);
        RenderStateKey other = new(snapshot.Desc with { SampleMask = 0 });
        dictionary.Add(other, 2);
        Assert.Equal(1, dictionary[new(snapshot.Desc)]);
        Assert.Equal(2, dictionary[other]);
    }

    /// <summary>Checks FormatsCountsPresenceAndLayoutCountsAreExact.</summary>
    [Fact]
    public void FormatsCountsPresenceAndLayoutCountsAreExact()
    {
        PipelineVariantKey Key(TextureFormat[] formats, TextureFormat? depth = null, DescriptorLayoutKey layout = default) => PipelineVariantKey.Create(default, formats, depth, layout);
        Assert.NotEqual(Key([]), Key([TextureFormat.Rgba8Unorm]));
        Assert.NotEqual(Key([TextureFormat.Rgba8Unorm, TextureFormat.R8Unorm]), Key([TextureFormat.R8Unorm, TextureFormat.Rgba8Unorm]));
        Assert.NotEqual(Key([]), Key([], TextureFormat.Rgba8Unorm));
        foreach (TextureFormat f in Enum.GetValues<TextureFormat>())
        {
            Assert.True((uint)f < 16);
            Assert.NotEqual(Key([], f), Key([f]));
        }

        for (int n = 0; n <= 8; n++)
        {
            var formats = new TextureFormat[n];
            PipelineVariantKey key = Key(formats);
            Assert.Equal(key, Key(new TextureFormat[n]));
            Assert.Equal(key.GetHashCode(), Key(new TextureFormat[n]).GetHashCode());
            if (n > 0)
            {
                formats[n - 1] = TextureFormat.R8Unorm;
                Assert.NotEqual(key, Key(formats));
            }
        }

        Assert.NotEqual(Key([], layout: new(1, 2)), Key([], layout: new(2, 1)));
        Assert.NotEqual(Key([], layout: new(1, 2)), Key([], layout: new(1, 3)));
        Assert.Equal(Key([], layout: new(uint.MaxValue, uint.MaxValue)), Key([], layout: new(uint.MaxValue, uint.MaxValue)));
    }

    /// <summary>Checks NonfiniteValuesStillFailAdmission.</summary>
    [Fact]
    public void NonfiniteValuesStillFailAdmission()
    {
        foreach (float value in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
        {
            Assert.Throws<ArgumentException>(() => PipelineValidation.State(new() { Rasterization = new() { DepthBiasSlope = value } }, _caps));
            Assert.Throws<ArgumentException>(() => PipelineValidation.State(new() { Rasterization = new() { DepthBiasClamp = value } }, _caps));
        }
    }

    /// <summary>Rejects oversized public state/pass calls before touching attachment resources.</summary>
    [Trait("Execution", "Gpu")]
    [GpuFact]
    public void PublicAdmissionRejectsNineAttachments()
    {
        using var device = VulkanDevice.Create();
        Assert.InRange(device.Caps.MaxColorAttachments, 1u, 8u);
        using IGraphicsCommandBuffer command = device.CreateCommandBuffer(new());
        Assert.Throws<ArgumentException>(() => command.BeginRenderPass(new() { ColorAttachments = new RenderColorAttachmentDesc[9] }));
        CheckStateAdmission(device, command);
        using var validation = new Lumyte.Graphics.Validation.ValidationDevice(device);
        Assert.Equal(device.Caps.MaxColorAttachments, validation.Caps.MaxColorAttachments);
        using IGraphicsCommandBuffer wrapped = validation.CreateCommandBuffer(new());
        Assert.ThrowsAny<ArgumentException>(() => wrapped.BeginRenderPass(new() { ColorAttachments = new RenderColorAttachmentDesc[9] }));
        CheckStateAdmission(validation, wrapped);
    }

    /// <summary>Default key hashing agrees with an explicitly encoded all-zero key.</summary>
    [Fact]
    public void DefaultKeyPreservesHashInvariant()
    {
        var zero = new RenderStateKey(new()
        {
            Topology = PrimitiveTopology.PointList,
            Rasterization = new() { DepthClipEnable = false },
            DepthStencil = new() { DepthCompare = CompareFunction.Never, Front = new() { Compare = CompareFunction.Never }, Back = new() { Compare = CompareFunction.Never }, StencilReadMask = 0, StencilWriteMask = 0 },
            SampleMask = 0,
        });
        Assert.Equal(default, zero);
        Assert.Equal(default(RenderStateKey).GetHashCode(), zero.GetHashCode());
    }

    /// <summary>Every fixed target lane contributes, including the last lane at capacity.</summary>
    [Fact]
    public void EveryTargetLaneContributes()
    {
        for (int count = 1; count <= 8; count++)
        {
            var state = GraphicsRenderStateDesc.ForColorTargets(count);
            RenderStateKey key = new(state);
            for (int lane = 0; lane < count; lane++)
            {
                ColorBlendStateDesc[] colors = state.ColorTargets.ToArray();
                colors[lane] = colors[lane] with { BlendEnable = true, WriteMask = ColorWriteMask.None };
                Assert.NotEqual(key, new RenderStateKey(state with { ColorTargets = colors }));
            }
        }
    }

    private static void CheckStateAdmission(IGraphicDevice device, IGraphicsCommandBuffer command)
    {
        using IGraphicsTexture target = device.CreateTexture(new() { Width = 8, Height = 8, Format = TextureFormat.Rgba8Unorm, Usage = TextureUsage.RenderAttachment });
        using IGraphicsTextureView view = target.CreateView();
        IRenderEncoder render = command.BeginRenderPass(new() { ColorAttachments = [new() { View = view, LoadOp = AttachmentLoadOp.Clear, StoreOp = AttachmentStoreOp.Store }] });
        Assert.Throws<NotSupportedException>(() => render.SetRenderState(GraphicsRenderStateDesc.ForColorTargets(9)));
        render.SetRenderState(GraphicsRenderStateDesc.ForColorTargets(1));
        render.End();
        command.Finish();
        command.Dispose();
    }

    private static int Size<T>()
        where T : unmanaged
        => Unsafe.SizeOf<T>();

    private static IEnumerable<GraphicsRenderStateDesc> Variants(GraphicsRenderStateDesc s)
    {
        foreach (PrimitiveTopology value in Enum.GetValues<PrimitiveTopology>())
        {
            if (value != s.Topology)
            {
                yield return s with { Topology = value };
            }
        }

        foreach (IndexFormat value in Enum.GetValues<IndexFormat>())
        {
            yield return s with { StripIndexFormat = value };
        }

        foreach (CullMode value in Enum.GetValues<CullMode>())
        {
            if (value != s.Rasterization.Cull)
            {
                yield return s with { Rasterization = s.Rasterization with { Cull = value } };
            }
        }

        yield return s with { Rasterization = s.Rasterization with { FrontFace = FrontFace.Clockwise } };
        yield return s with { Rasterization = s.Rasterization with { DepthClipEnable = false } };
        foreach (int value in new[] { -16777216, 16777216, -1, 1 })
        {
            yield return s with { Rasterization = s.Rasterization with { DepthBiasConstant = value } };
        }

        foreach (float value in new[] { -0f, float.MinValue, float.MaxValue, float.Epsilon })
        {
            yield return s with { Rasterization = s.Rasterization with { DepthBiasSlope = value } };
            yield return s with { Rasterization = s.Rasterization with { DepthBiasClamp = value } };
        }

        yield return s with { SampleMask = 0 };
        yield return s with { DepthStencil = s.DepthStencil with { DepthTestEnable = true } };
        yield return s with { DepthStencil = s.DepthStencil with { DepthWriteEnable = true } };
        yield return s with { DepthStencil = s.DepthStencil with { StencilTestEnable = true } };
        yield return s with { DepthStencil = s.DepthStencil with { StencilReadMask = 0 } };
        yield return s with { DepthStencil = s.DepthStencil with { StencilWriteMask = 0 } };
        foreach (CompareFunction value in Enum.GetValues<CompareFunction>())
        {
            if (value != s.DepthStencil.DepthCompare)
            {
                yield return s with { DepthStencil = s.DepthStencil with { DepthCompare = value } };
            }

            if (value != s.DepthStencil.Front.Compare)
            {
                yield return s with { DepthStencil = s.DepthStencil with { Front = s.DepthStencil.Front with { Compare = value } } };
                yield return s with { DepthStencil = s.DepthStencil with { Back = s.DepthStencil.Back with { Compare = value } } };
            }
        }

        foreach (StencilOperation value in Enum.GetValues<StencilOperation>())
        {
            if (value == StencilOperation.Keep)
            {
                continue;
            }

            yield return s with { DepthStencil = s.DepthStencil with { Front = s.DepthStencil.Front with { Fail = value } } };
            yield return s with { DepthStencil = s.DepthStencil with { Front = s.DepthStencil.Front with { DepthFail = value } } };
            yield return s with { DepthStencil = s.DepthStencil with { Front = s.DepthStencil.Front with { Pass = value } } };
            yield return s with { DepthStencil = s.DepthStencil with { Back = s.DepthStencil.Back with { Fail = value } } };
            yield return s with { DepthStencil = s.DepthStencil with { Back = s.DepthStencil.Back with { DepthFail = value } } };
            yield return s with { DepthStencil = s.DepthStencil with { Back = s.DepthStencil.Back with { Pass = value } } };
        }

        ColorBlendStateDesc c = s.ColorTargets[0];
        yield return s with { ColorTargets = [c with { BlendEnable = true }] };
        foreach (ColorWriteMask value in Enum.GetValues<ColorWriteMask>())
        {
            if (value != c.WriteMask)
            {
                yield return s with { ColorTargets = [c with { WriteMask = value }] };
            }
        }

        foreach (BlendFactor value in Enum.GetValues<BlendFactor>())
        {
            if (value != c.Color.Source)
            {
                yield return s with { ColorTargets = [c with { Color = c.Color with { Source = value } }] };
                yield return s with { ColorTargets = [c with { Alpha = c.Alpha with { Source = value } }] };
            }

            if (value != c.Color.Destination)
            {
                yield return s with { ColorTargets = [c with { Color = c.Color with { Destination = value } }] };
                yield return s with { ColorTargets = [c with { Alpha = c.Alpha with { Destination = value } }] };
            }
        }

        foreach (BlendOperation value in Enum.GetValues<BlendOperation>())
        {
            if (value == BlendOperation.Add)
            {
                continue;
            }

            yield return s with { ColorTargets = [c with { Color = c.Color with { Operation = value } }] };
            yield return s with { ColorTargets = [c with { Alpha = c.Alpha with { Operation = value } }] };
        }
    }

    private sealed class CollisionComparer : IEqualityComparer<RenderStateKey>
    {
        public bool Equals(RenderStateKey x, RenderStateKey y) => x.Equals(y);

        public int GetHashCode(RenderStateKey obj) => 0;
    }
}
