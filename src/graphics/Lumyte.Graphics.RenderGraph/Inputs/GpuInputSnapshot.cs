using System.Runtime.CompilerServices;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Shares declared CPU ownership with an immutable snapshot, never with a frame number.</summary>
internal sealed class GpuInputSnapshot
{
    private static readonly ConditionalWeakTable<object, ConditionalWeakTable<object, GpuInputSnapshot>> shared = new();
    private readonly Lazy<Retention> retention;
    private readonly ConditionalWeakTable<GpuRenderGraphPass, object> validated = new();
    private GpuInputSnapshot(object? value, Action<GpuRenderInputRetentionContext> retain)
    {
        Value = value;
        retention = new(() =>
        {
            List<IGpuUploadData> uploads = [];
            List<GpuRenderGraphResource> resources = [];
            List<GpuInputSnapshot> children = [];
            var context = new GpuRenderInputRetentionContext(uploads, resources, children);
            try { retain(context); }
            finally { context.Close(); }
            return new(uploads.ToArray(), resources.Distinct().ToArray(), children.Distinct().ToArray());
        });
    }
    internal object? Value { get; }
    internal static GpuInputSnapshot Create<T>(IGpuGraphInputContract<T> contract, T snapshot)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (typeof(T).IsValueType || snapshot is null)
        { return new(snapshot, context => contract.Retain(context, snapshot)); }
        // Both the contract and snapshot are weak keys. Sharing never makes an old frame a cache root.
        return shared.GetValue(contract, static _ => new()).GetValue(snapshot,
            _ => new(snapshot, context => contract.Retain(context, snapshot)));
    }
    internal void Validate(GpuRenderGraphPass pass)
    { if (!validated.TryGetValue(pass, out _)) { Validate(pass, []); } }
    private void Validate(GpuRenderGraphPass pass, HashSet<GpuInputSnapshot> path)
    {
        if (validated.TryGetValue(pass, out _)) { return; }
        if (!path.Add(this)) { throw new InvalidOperationException("Input ownership contains a cyclic snapshot dependency."); }
        foreach (var resource in retention.Value.Resources)
        {
            if (!pass.Uses.Any(use => ReferenceEquals(use.Resource, resource) && use.Access != GpuRenderGraphAccess.Write))
            { throw new InvalidOperationException($"Input in pass '{pass.Name}' references an undeclared read: '{resource.Name}'."); }
        }
        foreach (var child in retention.Value.Children) { child.Validate(pass, path); }
        path.Remove(this);
        validated.GetValue(pass, static _ => new());
    }
    private sealed record Retention(IGpuUploadData[] Uploads, GpuRenderGraphResource[] Resources, GpuInputSnapshot[] Children);
}
