using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph;

internal static class RenderGraphValidation
{
    internal const ResourceAccess Writes = ResourceAccess.HostWrite | ResourceAccess.CopyWrite | ResourceAccess.ShaderWrite | ResourceAccess.ColorWrite | ResourceAccess.DepthStencilWrite;

    internal static bool IsWrite(BarrierScope scope) => (scope.Access & Writes) != 0;

    internal static void Scope(BarrierScope scope, bool required)
    {
        const ResourceAccess Reads = ResourceAccess.HostRead | ResourceAccess.CopyRead | ResourceAccess.ShaderRead | ResourceAccess.ColorRead | ResourceAccess.DepthStencilRead | ResourceAccess.IndexRead | ResourceAccess.IndirectRead;
        if ((scope.Stages & ~(PipelineStage.AllCommands | PipelineStage.Host)) != 0 ||
            (scope.Access & ~(Reads | Writes)) != 0 || (required && scope.Access == ResourceAccess.None))
        {
            throw new ArgumentException("Unknown or empty resource access.");
        }

        Check(scope, ResourceAccess.HostRead | ResourceAccess.HostWrite, PipelineStage.Host);
        Check(scope, ResourceAccess.CopyRead | ResourceAccess.CopyWrite, PipelineStage.Copy);
        Check(scope, ResourceAccess.ShaderRead | ResourceAccess.ShaderWrite, PipelineStage.VertexShader | PipelineStage.FragmentShader | PipelineStage.ComputeShader);
        Check(scope, ResourceAccess.ColorRead | ResourceAccess.ColorWrite, PipelineStage.ColorOutput);
        Check(scope, ResourceAccess.DepthStencilRead | ResourceAccess.DepthStencilWrite, PipelineStage.DepthStencil);
        Check(scope, ResourceAccess.IndexRead, PipelineStage.IndexInput);
        Check(scope, ResourceAccess.IndirectRead, PipelineStage.DrawIndirect);
    }

    private static void Check(BarrierScope scope, ResourceAccess access, PipelineStage stages)
    {
        if ((scope.Access & access) != 0 && (scope.Stages & stages) == 0)
        {
            throw new ArgumentException("Resource access does not match its pipeline stages.");
        }
    }
}
