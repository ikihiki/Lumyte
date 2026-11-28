using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Returns session artifacts through the registered owner.</summary>
internal sealed class SessionOwner(GraphicsExecutionModule module, IGraphicDevice device, GraphicsPlanningBudget? budget = null) : IAsyncDisposable
{
    internal GraphicsDeviceSession Value { get; } = new(module, device, budget);

    public static implicit operator GraphicsDeviceSession(SessionOwner owner) => owner.Value;

    public ValueTask DisposeAsync() => Value.RetireAsync();

    internal GraphicsFrameScope CreateFrame() => Value.CreateFrame();

    internal ComputeKernel<T> ImportComputeKernel<T>(IGraphicsComputePipeline pipeline, IDisposable lease)
        where T : struct, IShaderArguments
        => Value.ImportComputeKernel<T>(pipeline, lease);

    internal RasterKernel<T> ImportRasterKernel<T>(IGraphicsPipeline pipeline, IDisposable lease)
        where T : struct, IShaderArguments
        => Value.ImportRasterKernel<T>(pipeline, lease);

    internal GraphicsExecution CreateExecution(PreparedGraphicsExecution prepared) => Value.CreateExecution(prepared);

    internal ValueTask DrainAsync() => Value.DrainAsync();

    internal ValueTask RecoverRetiredDeviceAsync() => Value.RecoverRetiredDeviceAsync();
}
