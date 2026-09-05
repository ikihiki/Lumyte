using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.TwoD;

public static class GpuFrameExtensions
{
    /// <summary>Adds a prepared drawing and retains its GPU buffers through submission completion.</summary>
    public static RenderPassResources AddTwoD(
        this GpuFrame frame,
        string name,
        Renderer renderer,
        PreparedDisplayList displayList,
        RenderTargetOptions options = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(displayList);
        IDisposable lease = displayList.AcquireLease();
        try
        {
            RenderPassResources resources = frame.Graph.AddTwoD(
                name,
                renderer,
                displayList,
                frame.TargetResource,
                options,
                markOutput: false);
            frame.Retain(lease);
            return resources;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }
}
