namespace Lumyte.Graphics.RenderGraph;

public sealed class GpuRenderGraphPassPlan
{
    internal GpuRenderGraphPassPlan(string name, int declarationIndex, GpuRenderGraphResourceAccess[] accesses)
    {
        Name = name;
        DeclarationIndex = declarationIndex;
        Accesses = Array.AsReadOnly(accesses);
    }

    public string Name { get; }
    public int DeclarationIndex { get; }
    public IReadOnlyList<GpuRenderGraphResourceAccess> Accesses { get; }
}
