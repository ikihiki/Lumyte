using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

/// <summary>Provides internal actual-allocation preflight for the generic pipeline adapter.</summary>
public sealed partial class RenderGraph
{
    internal void ValidateNativeOrigins(IGraphicDevice device)
    {
        ValidateAppend(device, Name);
        var allocations = new Dictionary<object, RenderGraphResource>();
        foreach (RenderGraphResource resource in _resources)
        {
            if (resource.Allocation is not { } allocation)
            {
                continue;
            }

            if (allocation is not IGraphicsNativeOrigin origin)
            {
                throw new NotSupportedException("Pipeline graph binding requires actual native origin metadata for imported allocations.");
            }

            origin.ValidateOrigin();
            if (!ReferenceEquals(origin.Device, device))
            {
                throw new ArgumentException("An imported allocation belongs to another actual native device.");
            }

            if (allocations.TryGetValue(origin.AllocationIdentity, out RenderGraphResource? previous) && !ReferenceEquals(previous, resource))
            {
                throw new ArgumentException("One actual native allocation was imported under multiple logical identities.");
            }

            allocations.Add(origin.AllocationIdentity, resource);
        }
    }
}
