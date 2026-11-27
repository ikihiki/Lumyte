using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Typed indexed, indirect, mesh and depth-capable command declarations.</summary>
public static class ExtendedPlanCommands
{
    /// <summary>Declares Draw with explicit resource contracts.</summary>
    /// <param name="pass">The pass.</param>
    /// <param name="kernel">The kernel.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="attachments">The attachments.</param>
    /// <param name="state">The state.</param>
    /// <param name="vertexCount">The vertexCount.</param>
    /// <param name="instanceCount">The instanceCount.</param>
    /// <param name="firstVertex">The firstVertex.</param>
    /// <param name="firstInstance">The firstInstance.</param>
    /// <typeparam name="TArguments">The shader argument ABI.</typeparam>
    public static void Draw<TArguments>(this PlanPass pass, RasterKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, uint vertexCount, uint instanceCount = 1, uint firstVertex = 0, uint firstInstance = 0)
        where TArguments : struct, IShaderArguments
    {
        ExtendedCommandValidation.Kernel(pass, kernel.Session);
        ExtendedCommandValidation.DrawCounts(vertexCount, instanceCount);
        _ = checked(firstVertex + vertexCount);
        _ = checked(firstInstance + instanceCount);
        Add<TArguments, DirectRasterOperation>(pass, arguments, attachments, state, new DirectRasterOperation(kernel.Pipeline, vertexCount, instanceCount, firstVertex, firstInstance));
    }

    /// <summary>Declares DrawIndexed with explicit resource contracts.</summary>
    /// <param name="pass">The pass.</param>
    /// <param name="kernel">The kernel.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="attachments">The attachments.</param>
    /// <param name="state">The state.</param>
    /// <param name="indices">The indices.</param>
    /// <param name="indexRange">The indexRange.</param>
    /// <param name="indexCount">The indexCount.</param>
    /// <param name="instanceCount">The instanceCount.</param>
    /// <param name="firstIndex">The firstIndex.</param>
    /// <param name="baseVertex">The baseVertex.</param>
    /// <param name="firstInstance">The firstInstance.</param>
    /// <typeparam name="TArguments">The shader argument ABI.</typeparam>
    public static void DrawIndexed<TArguments>(this PlanPass pass, RasterKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, PlanBuffer<ushort> indices, PlanRange indexRange, uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
        where TArguments : struct, IShaderArguments
    {
        ExtendedCommandValidation.Kernel(pass, kernel.Session);
        ExtendedCommandValidation.DrawCounts(indexCount, instanceCount);
        _ = checked(firstInstance + instanceCount);
        ExtendedCommandValidation.Index(pass, indices, indexRange, indexCount, firstIndex);
        Add<TArguments, IndexedRasterOperation>(pass, arguments, attachments, state, new IndexedRasterOperation(kernel.Pipeline, new Index16Binding(indices, indexRange), indexCount, instanceCount, firstIndex, baseVertex, firstInstance));
    }

    /// <summary>Declares DrawIndexedIndirect with explicit resource contracts.</summary>
    /// <param name="pass">The pass.</param>
    /// <param name="kernel">The kernel.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="attachments">The attachments.</param>
    /// <param name="state">The state.</param>
    /// <param name="indices">The indices.</param>
    /// <param name="indexRange">The indexRange.</param>
    /// <param name="indirect">The indirect.</param>
    /// <param name="indirectRange">The indirectRange.</param>
    /// <typeparam name="TArguments">The shader argument ABI.</typeparam>
    public static void DrawIndexedIndirect<TArguments>(this PlanPass pass, RasterKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, PlanBuffer<ushort> indices, PlanRange indexRange, PlanBuffer<DrawIndexedIndirectArguments> indirect, PlanRange indirectRange)
        where TArguments : struct, IShaderArguments
    {
        ExtendedCommandValidation.Kernel(pass, kernel.Session);
        ExtendedCommandValidation.Index(pass, indices, indexRange, 1, 0);
        ExtendedCommandValidation.Indirect(pass, indirect, indirectRange);
        Add<TArguments, IndexedIndirectRasterOperation>(pass, arguments, attachments, state, new IndexedIndirectRasterOperation(kernel.Pipeline, new Index16Binding(indices, indexRange), indirect, indirectRange));
    }

    /// <summary>Declares DrawIndexed with explicit resource contracts.</summary>
    /// <param name="pass">The pass.</param>
    /// <param name="kernel">The kernel.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="attachments">The attachments.</param>
    /// <param name="state">The state.</param>
    /// <param name="indices">The indices.</param>
    /// <param name="indexRange">The indexRange.</param>
    /// <param name="indexCount">The indexCount.</param>
    /// <param name="instanceCount">The instanceCount.</param>
    /// <param name="firstIndex">The firstIndex.</param>
    /// <param name="baseVertex">The baseVertex.</param>
    /// <param name="firstInstance">The firstInstance.</param>
    /// <typeparam name="TArguments">The shader argument ABI.</typeparam>
    public static void DrawIndexed<TArguments>(this PlanPass pass, RasterKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, PlanBuffer<uint> indices, PlanRange indexRange, uint indexCount, uint instanceCount = 1, uint firstIndex = 0, int baseVertex = 0, uint firstInstance = 0)
        where TArguments : struct, IShaderArguments
    {
        ExtendedCommandValidation.Kernel(pass, kernel.Session);
        ExtendedCommandValidation.DrawCounts(indexCount, instanceCount);
        _ = checked(firstInstance + instanceCount);
        ExtendedCommandValidation.Index(pass, indices, indexRange, indexCount, firstIndex);
        Add<TArguments, IndexedRasterOperation>(pass, arguments, attachments, state, new IndexedRasterOperation(kernel.Pipeline, new Index32Binding(indices, indexRange), indexCount, instanceCount, firstIndex, baseVertex, firstInstance));
    }

    /// <summary>Declares DrawIndexedIndirect with explicit resource contracts.</summary>
    /// <param name="pass">The pass.</param>
    /// <param name="kernel">The kernel.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="attachments">The attachments.</param>
    /// <param name="state">The state.</param>
    /// <param name="indices">The indices.</param>
    /// <param name="indexRange">The indexRange.</param>
    /// <param name="indirect">The indirect.</param>
    /// <param name="indirectRange">The indirectRange.</param>
    /// <typeparam name="TArguments">The shader argument ABI.</typeparam>
    public static void DrawIndexedIndirect<TArguments>(this PlanPass pass, RasterKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, PlanBuffer<uint> indices, PlanRange indexRange, PlanBuffer<DrawIndexedIndirectArguments> indirect, PlanRange indirectRange)
        where TArguments : struct, IShaderArguments
    {
        ExtendedCommandValidation.Kernel(pass, kernel.Session);
        ExtendedCommandValidation.Index(pass, indices, indexRange, 1, 0);
        ExtendedCommandValidation.Indirect(pass, indirect, indirectRange);
        Add<TArguments, IndexedIndirectRasterOperation>(pass, arguments, attachments, state, new IndexedIndirectRasterOperation(kernel.Pipeline, new Index32Binding(indices, indexRange), indirect, indirectRange));
    }

    /// <summary>Declares DrawIndirect with explicit resource contracts.</summary>
    /// <param name="pass">The pass.</param>
    /// <param name="kernel">The kernel.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="attachments">The attachments.</param>
    /// <param name="state">The state.</param>
    /// <param name="indirect">The indirect.</param>
    /// <param name="indirectRange">The indirectRange.</param>
    /// <typeparam name="TArguments">The shader argument ABI.</typeparam>
    public static void DrawIndirect<TArguments>(this PlanPass pass, RasterKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, PlanBuffer<DrawIndirectArguments> indirect, PlanRange indirectRange)
        where TArguments : struct, IShaderArguments
    {
        ExtendedCommandValidation.Kernel(pass, kernel.Session);
        ExtendedCommandValidation.Indirect(pass, indirect, indirectRange);
        Add<TArguments, IndirectRasterOperation>(pass, arguments, attachments, state, new IndirectRasterOperation(kernel.Pipeline, indirect, indirectRange));
    }

    /// <summary>Declares MeshTasks with explicit resource contracts.</summary>
    /// <param name="pass">The pass.</param>
    /// <param name="kernel">The kernel.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="attachments">The attachments.</param>
    /// <param name="state">The state.</param>
    /// <param name="x">The x.</param>
    /// <param name="y">The y.</param>
    /// <param name="z">The z.</param>
    /// <typeparam name="TArguments">The shader argument ABI.</typeparam>
    public static void MeshTasks<TArguments>(this PlanPass pass, MeshKernel<TArguments> kernel, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, uint x, uint y = 1, uint z = 1)
        where TArguments : struct, IShaderArguments
    {
        ExtendedCommandValidation.Kernel(pass, kernel.Session);
        ExtendedCommandValidation.Counts(x, y, z);
        Add<TArguments, MeshRasterOperation>(pass, arguments, attachments, state, new MeshRasterOperation(kernel.Pipeline, x, y, z));
    }

    /// <summary>Declares DispatchIndirect with explicit resource contracts.</summary>
    /// <param name="pass">The pass.</param>
    /// <param name="kernel">The kernel.</param>
    /// <param name="arguments">The arguments.</param>
    /// <param name="indirect">The indirect.</param>
    /// <param name="indirectRange">The indirectRange.</param>
    /// <typeparam name="TArguments">The shader argument ABI.</typeparam>
    public static void DispatchIndirect<TArguments>(this PlanPass pass, ComputeKernel<TArguments> kernel, in TArguments arguments, PlanBuffer<DispatchIndirectArguments> indirect, PlanRange indirectRange)
        where TArguments : struct, IShaderArguments
    {
        ExtendedCommandValidation.Kernel(pass, kernel.Session);
        ExtendedCommandValidation.Indirect(pass, indirect, indirectRange);
        pass.Owner.ReserveCommand(checked((ulong)TArguments.TransferSize), TArguments.ReferenceCount);
        ArgumentCaptureWriter writer = new(pass, TArguments.TransferSize, TArguments.ReferenceCount);
        arguments.Write(ref writer);
        writer.Complete<TArguments>();
        pass.Commands.Add(new IndirectComputeCommand<TArguments>(kernel, writer.Pieces, indirect, indirectRange));
    }

    private static void Add<TArguments, TOperation>(PlanPass pass, in TArguments arguments, RasterAttachments attachments, CompleteRasterState state, TOperation operation)
        where TArguments : struct, IShaderArguments
        where TOperation : IRasterOperation
    {
        ExtendedCommandValidation.Raster(pass, attachments, state);
        pass.Owner.ReserveCommand(checked((ulong)TArguments.TransferSize), TArguments.ReferenceCount);
        RasterAttachments capturedAttachments = attachments.Capture();
        CompleteRasterState capturedState = state.Capture();
        ArgumentCaptureWriter writer = new(pass, TArguments.TransferSize, TArguments.ReferenceCount);
        arguments.Write(ref writer);
        writer.Complete<TArguments>();
        pass.Commands.Add(new ExtendedRasterCommand<TArguments, TOperation>(writer.Pieces, capturedAttachments, capturedState, operation));
        foreach (RasterColorAttachment color in capturedAttachments.Colors.Where(c => c.Store == AttachmentStoreOp.Discard))
        {
            pass.DiscardContents(color.Texture, color.Texture.Range);
        }

        if (capturedAttachments.DepthStencil is { } depth && (depth.DepthStore == AttachmentStoreOp.Discard || (depth.Texture.Identity.TextureFormat == TextureFormat.Depth24Stencil8 && depth.StencilStore == AttachmentStoreOp.Discard)))
        {
            pass.DiscardContents(depth.Texture, depth.Texture.Range);
        }
    }
}
