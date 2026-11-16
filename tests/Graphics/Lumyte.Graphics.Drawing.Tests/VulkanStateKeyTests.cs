using System.Reflection;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Vulkan;
using Xunit;

namespace Lumyte.Graphics.Drawing.Tests;

/// <summary>Checks every state field participates in cache identity and snapshots detach caller collections.</summary>
public sealed class VulkanStateKeyTests
{
    /// <summary>Checks structural identity against independently changed public fields, including signed zero.</summary>
    [Fact]
    public void EveryStateFieldAndColorOrderAffectsIdentity()
    {
        var state = new GraphicsRenderStateDesc { Topology = PrimitiveTopology.TriangleStrip, StripIndexFormat = IndexFormat.Uint16, ColorTargets = new ColorBlendStateDesc[] { new(), new() { BlendEnable = true } } };
        object baseline = Key(state);
        Assert.NotEqual(baseline, Key(state with { StripIndexFormat = null }));
        Assert.NotEqual(baseline, Key(state with { ColorTargets = Array.Empty<ColorBlendStateDesc>() }));
        object equivalent = Key(state with { Rasterization = state.Rasterization with { }, DepthStencil = state.DepthStencil with { Front = state.DepthStencil.Front with { }, Back = state.DepthStencil.Back with { } }, ColorTargets = state.ColorTargets.Select(c => c with { Color = c.Color with { }, Alpha = c.Alpha with { } }).ToArray() });
        Assert.Equal(baseline, equivalent);
        Assert.Equal(baseline.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(baseline, Key(state with { ColorTargets = state.ColorTargets.Take(1).ToArray() }));
        var cache = new Dictionary<object, int> { [baseline] = 1 };
        cache[equivalent] = 2;
        Assert.Single(cache);
        Assert.Equal(2, cache[baseline]);
        int count = 0;
        foreach (GraphicsRenderStateDesc changed in Variants(state).Cast<GraphicsRenderStateDesc>())
        {
            object changedKey = Key(changed);
            Assert.NotEqual(baseline, changedKey);
            cache.Add(changedKey, count + 3);
            count++;
        }

        Assert.Equal(count + 1, cache.Count);
        Assert.True(count >= 30, $"Only {count} state mutations checked.");
        Assert.NotEqual(baseline, Key(state with { ColorTargets = state.ColorTargets.Reverse().ToArray() }));
        Assert.NotEqual(baseline, Key(state with { Rasterization = state.Rasterization with { DepthBiasSlope = -0.0f } }));
        Assert.NotEqual(baseline, Key(state with { Rasterization = state.Rasterization with { DepthBiasClamp = -0.0f } }));
    }

    /// <summary>Preserves immutable snapshot identity after a caller replaces a color target.</summary>
    [Fact]
    public void SnapshotDetachesCollectionAndRejectsNonfiniteBias()
    {
        ColorBlendStateDesc[] targets = [new()];
        var state = new GraphicsRenderStateDesc { ColorTargets = targets };
        object snapshot = Snapshot(state);
        object captured = snapshot.GetType().GetProperty("Key")!.GetValue(snapshot)!;
        GraphicsRenderStateDesc capturedDesc = Assert.IsType<GraphicsRenderStateDesc>(snapshot.GetType().GetProperty("Desc")!.GetValue(snapshot));
        targets[0] = new() { WriteMask = 0 };
        Assert.Equal(ColorWriteMask.All, capturedDesc.ColorTargets[0].WriteMask);
        Assert.Equal(captured, Key(new() { ColorTargets = new ColorBlendStateDesc[] { new() } }));
        Assert.NotEqual(captured, Key(state));
        Assert.Throws<TargetInvocationException>(() => Key(state with { Rasterization = new() { DepthBiasSlope = float.NaN } }));
        Assert.Throws<TargetInvocationException>(() => Key(state with { Rasterization = new() { DepthBiasClamp = float.PositiveInfinity } }));
    }

    private static object Key(GraphicsRenderStateDesc state)
    {
        object snapshot = Snapshot(state);
        return snapshot.GetType().GetProperty("Key")!.GetValue(snapshot)!;
    }

    private static object Snapshot(GraphicsRenderStateDesc state)
    {
        Type validation = typeof(VulkanDevice).Assembly.GetType("Lumyte.Graphics.Vulkan.PipelineValidation", true)!;
        return validation.GetMethod("State", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [state, new DeviceCaps { Features = GraphicsFeatures.DepthBiasClamp }])!;
    }

    private static IEnumerable<object> Variants(object source)
    {
        foreach (PropertyInfo property in source.GetType().GetProperties().Where(p => p.SetMethod is not null))
        {
            if (property.Name == nameof(RasterizationStateDesc.DepthClipEnable))
            {
                continue;
            }

            object? current = property.GetValue(source);
            IEnumerable<object?> values;
            if (property.PropertyType == typeof(bool))
            {
                values = [!(bool)current!];
            }
            else if (property.PropertyType == typeof(PrimitiveTopology))
            {
                values = [PrimitiveTopology.LineStrip];
            }
            else if (property.PropertyType.IsEnum)
            {
                values = Enum.GetValues(property.PropertyType).Cast<object>().Where(v => !v.Equals(current)).Take(1);
            }
            else if (property.PropertyType == typeof(uint))
            {
                values = [(uint)current! == 0 ? 1u : 0u];
            }
            else if (property.PropertyType == typeof(int))
            {
                values = [(int)current! + 1];
            }
            else if (property.PropertyType == typeof(float))
            {
                values = [(float)current! + 0.5f];
            }
            else if (property.PropertyType == typeof(IndexFormat?))
            {
                values = [IndexFormat.Uint32];
            }
            else if (current is IReadOnlyList<ColorBlendStateDesc> colors)
            {
                values = Variants(colors[0]).Select(v => (object)new ColorBlendStateDesc[] { (ColorBlendStateDesc)v, colors[1] });
            }
            else
            {
                values = current is null ? [] : Variants(current);
            }

            foreach (object? value in values)
            {
                object clone = source.GetType().GetMethod("<Clone>$")!.Invoke(source, null)!;
                property.SetValue(clone, value);
                yield return clone;
            }
        }
    }
}
