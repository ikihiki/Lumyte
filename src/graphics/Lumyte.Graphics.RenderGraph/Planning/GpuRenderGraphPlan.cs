using System.Collections.Frozen;

namespace Lumyte.Graphics.RenderGraph;

public sealed class GpuRenderGraphPlan
{
    private long nextBindingsGeneration;
    internal readonly IReadOnlyList<GpuGraphInput> Inputs;
    internal GpuRenderGraphPlan(GpuRenderGraphPass[] passes, GpuRenderGraphResource[] resources,
        GpuGraphInput[] inputs, IEnumerable<GpuRenderGraphResource> exports, IEnumerable<GpuRenderGraphResource> outputs)
    {
        Passes = Array.AsReadOnly(passes);
        Resources = Array.AsReadOnly(resources);
        Inputs = Array.AsReadOnly(inputs);
        Exports = exports.ToFrozenSet();
        Outputs = outputs.ToFrozenSet();
    }
    public IReadOnlyList<GpuRenderGraphPass> Passes { get; }
    public IReadOnlyList<GpuRenderGraphResource> Resources { get; }
    public IReadOnlySet<GpuRenderGraphResource> Exports { get; }
    public IReadOnlySet<GpuRenderGraphResource> Outputs { get; }
    public GpuRenderGraphBindingsBuilder CreateBindings() => new(this);
    internal long NextBindingsGeneration() => Interlocked.Increment(ref nextBindingsGeneration);
    public ValueTask<GpuRenderGraphExecution> SubmitAsync(IGpuRenderRuntime runtime,
        GpuRenderGraphBindings? bindings = null, CancellationToken cancellationToken = default)
        => runtime.SubmitAsync(this, bindings, cancellationToken);

    /// <summary>Provider extension: resolve live CPU/resource slots before preparing any GPU work.</summary>
    public GpuRenderGraphBindings ValidateBindings(GpuRenderGraphBindings? bindings = null)
        => ValidateBindingsExcept(bindings, null);

    internal GpuRenderGraphBindings ValidateBindingsExcept(GpuRenderGraphBindings? bindings, GpuRenderGraphResource? omitted)
    {
        bindings ??= CreateBindings().Build();
        if (!ReferenceEquals(bindings.Plan, this)) { throw new ArgumentException("Bindings belong to another plan.", nameof(bindings)); }
        foreach (var input in Inputs)
        {
            if (!bindings.InputValues.ContainsKey(input)) { throw new InvalidOperationException($"Input '{input.Name}' has no value."); }
        }
        var aliases = new HashSet<(Guid Runtime, Guid Resource)>();
        foreach (var resource in Resources)
        {
            if (ReferenceEquals(resource, omitted)) { continue; }
            if (bindings.ResolveResource(resource) is not { } reference) { continue; }
            var matches = (resource, reference) switch
            {
                (GpuRenderGraphTexture texture, GpuGraphTextureRef textureRef) => texture.Description == textureRef.Description,
                (GpuRenderGraphBuffer buffer, GpuGraphBufferRef bufferRef) => buffer.Description == bufferRef.Description,
                _ => false
            };
            if (!matches) { throw new ArgumentException($"Resource '{resource.Name}' does not match its declared description.", nameof(bindings)); }
            if (!aliases.Add((reference.RuntimeId, reference.Identity)))
            { throw new ArgumentException($"Resource '{resource.Name}' aliases another logical declaration. Share the same logical resource instead.", nameof(bindings)); }
        }
        return bindings;
    }
}
