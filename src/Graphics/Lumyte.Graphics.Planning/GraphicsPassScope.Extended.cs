using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Expiring typed indexed, indirect, mesh and depth-capable command declarations.</summary>
public sealed partial class GraphicsPassScope
{
    /// <summary>Declares Draw through the expiring stage authority.</summary>
    /// <typeparam name="TArguments">The exact shader argument ABI.</typeparam>
    /// <param name="kernel">The explicit kernel contract.</param>
    /// <param name="arguments">The explicit arguments contract.</param>
    /// <param name="attachments">The explicit attachments contract.</param>
    /// <param name="state">The explicit state contract.</param>
    /// <param name="vertexCount">The explicit vertexCount contract.</param>
    /// <param name="instanceCount">The explicit instanceCount contract.</param>
    /// <param name="firstVertex">The explicit firstVertex contract.</param>
    /// <param name="firstInstance">The explicit firstInstance contract.</param>
    public void Draw<TArguments>(RasterKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        where TArguments : struct, IShaderArguments
    {
        _scope.Validate();
        _pass.Draw(kernel, arguments, attachments, state, vertexCount, instanceCount, firstVertex, firstInstance);
    }

    /// <summary>Declares DrawIndexed through the expiring stage authority.</summary>
    /// <typeparam name="TArguments">The exact shader argument ABI.</typeparam>
    /// <param name="kernel">The explicit kernel contract.</param>
    /// <param name="arguments">The explicit arguments contract.</param>
    /// <param name="attachments">The explicit attachments contract.</param>
    /// <param name="state">The explicit state contract.</param>
    /// <param name="indices">The explicit indices contract.</param>
    /// <param name="indexRange">The explicit indexRange contract.</param>
    /// <param name="indexCount">The explicit indexCount contract.</param>
    /// <param name="instanceCount">The explicit instanceCount contract.</param>
    /// <param name="firstIndex">The explicit firstIndex contract.</param>
    /// <param name="baseVertex">The explicit baseVertex contract.</param>
    /// <param name="firstInstance">The explicit firstInstance contract.</param>
    public void DrawIndexed<TArguments>(RasterKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, PlanBuffer<ushort> indices, PlanRange indexRange, uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
        where TArguments : struct, IShaderArguments
    {
        _scope.Validate();
        _pass.DrawIndexed(kernel, arguments, attachments, state, indices, indexRange, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    /// <summary>Declares DrawIndexedIndirect through the expiring stage authority.</summary>
    /// <typeparam name="TArguments">The exact shader argument ABI.</typeparam>
    /// <param name="kernel">The explicit kernel contract.</param>
    /// <param name="arguments">The explicit arguments contract.</param>
    /// <param name="attachments">The explicit attachments contract.</param>
    /// <param name="state">The explicit state contract.</param>
    /// <param name="indices">The explicit indices contract.</param>
    /// <param name="indexRange">The explicit indexRange contract.</param>
    /// <param name="indirect">The explicit indirect contract.</param>
    /// <param name="indirectRange">The explicit indirectRange contract.</param>
    public void DrawIndexedIndirect<TArguments>(RasterKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, PlanBuffer<ushort> indices, PlanRange indexRange, PlanBuffer<DrawIndexedIndirectArguments> indirect, PlanRange indirectRange)
        where TArguments : struct, IShaderArguments
    {
        _scope.Validate();
        _pass.DrawIndexedIndirect(kernel, arguments, attachments, state, indices, indexRange, indirect, indirectRange);
    }

    /// <summary>Declares DrawIndexed through the expiring stage authority.</summary>
    /// <typeparam name="TArguments">The exact shader argument ABI.</typeparam>
    /// <param name="kernel">The explicit kernel contract.</param>
    /// <param name="arguments">The explicit arguments contract.</param>
    /// <param name="attachments">The explicit attachments contract.</param>
    /// <param name="state">The explicit state contract.</param>
    /// <param name="indices">The explicit indices contract.</param>
    /// <param name="indexRange">The explicit indexRange contract.</param>
    /// <param name="indexCount">The explicit indexCount contract.</param>
    /// <param name="instanceCount">The explicit instanceCount contract.</param>
    /// <param name="firstIndex">The explicit firstIndex contract.</param>
    /// <param name="baseVertex">The explicit baseVertex contract.</param>
    /// <param name="firstInstance">The explicit firstInstance contract.</param>
    public void DrawIndexed<TArguments>(RasterKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, PlanBuffer<uint> indices, PlanRange indexRange, uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
        where TArguments : struct, IShaderArguments
    {
        _scope.Validate();
        _pass.DrawIndexed(kernel, arguments, attachments, state, indices, indexRange, indexCount, instanceCount, firstIndex, baseVertex, firstInstance);
    }

    /// <summary>Declares DrawIndexedIndirect through the expiring stage authority.</summary>
    /// <typeparam name="TArguments">The exact shader argument ABI.</typeparam>
    /// <param name="kernel">The explicit kernel contract.</param>
    /// <param name="arguments">The explicit arguments contract.</param>
    /// <param name="attachments">The explicit attachments contract.</param>
    /// <param name="state">The explicit state contract.</param>
    /// <param name="indices">The explicit indices contract.</param>
    /// <param name="indexRange">The explicit indexRange contract.</param>
    /// <param name="indirect">The explicit indirect contract.</param>
    /// <param name="indirectRange">The explicit indirectRange contract.</param>
    public void DrawIndexedIndirect<TArguments>(RasterKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, PlanBuffer<uint> indices, PlanRange indexRange, PlanBuffer<DrawIndexedIndirectArguments> indirect, PlanRange indirectRange)
        where TArguments : struct, IShaderArguments
    {
        _scope.Validate();
        _pass.DrawIndexedIndirect(kernel, arguments, attachments, state, indices, indexRange, indirect, indirectRange);
    }

    /// <summary>Declares DrawIndirect through the expiring stage authority.</summary>
    /// <typeparam name="TArguments">The exact shader argument ABI.</typeparam>
    /// <param name="kernel">The explicit kernel contract.</param>
    /// <param name="arguments">The explicit arguments contract.</param>
    /// <param name="attachments">The explicit attachments contract.</param>
    /// <param name="state">The explicit state contract.</param>
    /// <param name="indirect">The explicit indirect contract.</param>
    /// <param name="indirectRange">The explicit indirectRange contract.</param>
    public void DrawIndirect<TArguments>(RasterKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, PlanBuffer<DrawIndirectArguments> indirect, PlanRange indirectRange)
        where TArguments : struct, IShaderArguments
    {
        _scope.Validate();
        _pass.DrawIndirect(kernel, arguments, attachments, state, indirect, indirectRange);
    }

    /// <summary>Declares MeshTasks through the expiring stage authority.</summary>
    /// <typeparam name="TArguments">The exact shader argument ABI.</typeparam>
    /// <param name="kernel">The explicit kernel contract.</param>
    /// <param name="arguments">The explicit arguments contract.</param>
    /// <param name="attachments">The explicit attachments contract.</param>
    /// <param name="state">The explicit state contract.</param>
    /// <param name="x">The explicit x contract.</param>
    /// <param name="y">The explicit y contract.</param>
    /// <param name="z">The explicit z contract.</param>
    public void MeshTasks<TArguments>(MeshKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, uint x, uint y = 1, uint z = 1)
        where TArguments : struct, IShaderArguments
    {
        _scope.Validate();
        _pass.MeshTasks(kernel, arguments, attachments, state, x, y, z);
    }

    /// <summary>Declares DispatchIndirect through the expiring stage authority.</summary>
    /// <typeparam name="TArguments">The exact shader argument ABI.</typeparam>
    /// <param name="kernel">The explicit kernel contract.</param>
    /// <param name="arguments">The explicit arguments contract.</param>
    /// <param name="indirect">The explicit indirect contract.</param>
    /// <param name="indirectRange">The explicit indirectRange contract.</param>
    public void DispatchIndirect<TArguments>(ComputeKernel<TArguments> kernel, in TArguments arguments, PlanBuffer<DispatchIndirectArguments> indirect, PlanRange indirectRange)
        where TArguments : struct, IShaderArguments
    {
        _scope.Validate();
        _pass.DispatchIndirect(kernel, arguments, indirect, indirectRange);
    }
}
