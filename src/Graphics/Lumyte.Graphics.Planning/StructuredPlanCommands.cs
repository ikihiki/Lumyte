using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares structured plan commands.</summary>
public static class StructuredPlanCommands
{
    /// <summary>Declares copy.</summary>
    /// <param name = "pass">The pass.</param>
    /// <param name = "source">The source.</param>
    /// <param name = "sourceRange">The source range.</param>
    /// <param name = "destination">The destination.</param>
    /// <param name = "destinationRange">The destination range.</param>
    /// <typeparam name = "T">The typed shader or resource contract.</typeparam>
    public static void Copy<T>(this PlanPass pass, PlanBuffer<T> source, PlanRange sourceRange, PlanBuffer<T> destination, PlanRange destinationRange)
        where T : unmanaged
    {
        pass.Owner.Mutable();
        pass.Require(source, sourceRange, false);
        pass.Require(destination, destinationRange, true);
        if (source.Identity == destination.Identity)
        {
            throw new NotSupportedException("Native buffer copies require distinct allocations; use an explicit intermediate resource.");
        }

        if (sourceRange.Count != destinationRange.Count)
        {
            throw new ArgumentException("Copy counts differ.");
        }

        pass.Owner.ReserveCommand();
        pass.Commands.Add(new CopyBufferCommand<T>(source, sourceRange, destination, destinationRange));
    }

    /// <summary>Declares dispatch.</summary>
    /// <param name = "pass">The pass.</param>
    /// <param name = "kernel">The kernel.</param>
    /// <param name = "arguments">The arguments.</param>
    /// <param name = "x">The x.</param>
    /// <param name = "y">The y.</param>
    /// <param name = "z">The z.</param>
    /// <typeparam name = "TArguments">The typed shader or resource contract.</typeparam>
    public static void Dispatch<TArguments>(this PlanPass pass, ComputeKernel<TArguments> kernel, in TArguments arguments, uint x, uint y = 1, uint z = 1)
        where TArguments : struct, IShaderArguments
    {
        pass.Owner.Mutable();
        if (kernel.Session != pass.Owner.Frame.Session || x == 0 || y == 0 || z == 0)
        {
            throw new ArgumentException("Foreign kernel or empty dispatch.");
        }

        pass.Owner.ReserveCommand(checked((ulong)TArguments.TransferSize), TArguments.ReferenceCount);
        ArgumentCaptureWriter writer = new(pass, TArguments.TransferSize, TArguments.ReferenceCount);
        arguments.Write(ref writer);
        writer.Complete<TArguments>();
        pass.Commands.Add(new ComputeCommand<TArguments>(kernel, writer.Pieces, x, y, z));
    }

    /// <summary>Declares draw.</summary>
    /// <param name = "pass">The pass.</param>
    /// <param name = "kernel">The kernel.</param>
    /// <param name = "arguments">The arguments.</param>
    /// <param name = "target">The target.</param>
    /// <param name = "raster">The raster.</param>
    /// <param name = "vertexCount">The vertex count.</param>
    /// <param name = "instanceCount">The instance count.</param>
    /// <param name = "load">The load.</param>
    /// <param name = "clear">The clear.</param>
    /// <typeparam name = "TArguments">The typed shader or resource contract.</typeparam>
    public static void Draw<TArguments>(this PlanPass pass, RasterKernel<TArguments> kernel, in TArguments arguments, PlanTexture target, GraphicsRasterState raster, uint vertexCount, uint instanceCount = 1, AttachmentLoadOp load = AttachmentLoadOp.Clear, ClearColor clear = default)
        where TArguments : struct, IShaderArguments
    {
        ExtendedPlanCommands.Draw(
            pass,
            kernel,
            arguments,
            new RasterAttachments { Colors = [new() { Texture = target, Load = load, Store = AttachmentStoreOp.Store, Clear = clear }] },
            new CompleteRasterState { Raster = raster },
            vertexCount,
            instanceCount);
    }
}
