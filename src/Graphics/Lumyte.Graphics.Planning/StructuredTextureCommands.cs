using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares structured texture commands.</summary>
public static class StructuredTextureCommands
{
    /// <summary>Declares clear.</summary>
    /// <param name = "pass">The pass.</param>
    /// <param name = "target">The target.</param>
    /// <param name = "color">The color.</param>
    public static void Clear(this PlanPass pass, PlanTexture target, ClearColor color)
    {
        pass.Owner.Mutable();
        if (!pass.Uses.Any(u => u.Resource.SameVersion(target) && u.Range.Contains(target.Range) && u.Access != PlanAccess.Read && (u.Scope.Stages & PipelineStage.ColorOutput) != 0 && (u.Scope.Access & ResourceAccess.ColorWrite) != 0 && u.TextureState == TextureState.ColorAttachment))
        {
            throw new InvalidOperationException("Clear requires a matching predeclared target range, color-output stage, color-write access and attachment state.");
        }

        pass.Owner.ReserveCommand();
        pass.Commands.Add(new ClearTextureCommand(target, color));
    }

    /// <summary>Declares copy texture.</summary>
    /// <param name = "pass">The pass.</param>
    /// <param name = "source">The source.</param>
    /// <param name = "sourceRegion">The source region.</param>
    /// <param name = "destination">The destination.</param>
    /// <param name = "destinationRegion">The destination region.</param>
    public static void CopyTexture(this PlanPass pass, PlanTexture source, PlanTextureRegion sourceRegion, PlanTexture destination, PlanTextureRegion destinationRegion)
    {
        pass.Owner.Mutable();
        pass.RequireCopy(source, sourceRegion.Range(source), false, TextureState.CopySource);
        pass.RequireCopy(destination, destinationRegion.Range(destination), true, TextureState.CopyDestination);
        if (sourceRegion.Width != destinationRegion.Width || sourceRegion.Height != destinationRegion.Height || sourceRegion.ArrayLayerCount != destinationRegion.ArrayLayerCount)
        {
            throw new ArgumentException("Copy extents differ.");
        }

        pass.Owner.ReserveCommand();
        pass.Commands.Add(new CopyTextureCommand(source, sourceRegion, destination, destinationRegion));
    }

    /// <summary>Declares copy buffer to texture.</summary>
    /// <param name = "pass">The pass.</param>
    /// <param name = "source">The source.</param>
    /// <param name = "range">The range.</param>
    /// <param name = "bytesPerRow">The bytes per row.</param>
    /// <param name = "rowsPerImage">The rows per image.</param>
    /// <param name = "destination">The destination.</param>
    /// <param name = "region">The region.</param>
    public static void CopyBufferToTexture(this PlanPass pass, PlanBuffer<byte> source, PlanRange range, uint bytesPerRow, uint rowsPerImage, PlanTexture destination, PlanTextureRegion region)
    {
        pass.Owner.Mutable();
        pass.RequireCopy(source, range, false);
        pass.RequireCopy(destination, region.Range(destination), true, TextureState.CopyDestination);
        pass.Owner.ReserveCommand();
        pass.Commands.Add(new BufferTextureCommand(source, range, bytesPerRow, rowsPerImage, destination, region, true));
    }

    /// <summary>Declares copy texture to buffer.</summary>
    /// <param name = "pass">The pass.</param>
    /// <param name = "source">The source.</param>
    /// <param name = "region">The region.</param>
    /// <param name = "destination">The destination.</param>
    /// <param name = "range">The range.</param>
    /// <param name = "bytesPerRow">The bytes per row.</param>
    /// <param name = "rowsPerImage">The rows per image.</param>
    public static void CopyTextureToBuffer(this PlanPass pass, PlanTexture source, PlanTextureRegion region, PlanBuffer<byte> destination, PlanRange range, uint bytesPerRow, uint rowsPerImage)
    {
        pass.Owner.Mutable();
        pass.RequireCopy(source, region.Range(source), false, TextureState.CopySource);
        pass.RequireCopy(destination, range, true);
        pass.Owner.ReserveCommand();
        pass.Commands.Add(new BufferTextureCommand(destination, range, bytesPerRow, rowsPerImage, source, region, false));
    }

    internal static void RequireState(PlanPass pass, PlanTexture texture, TextureState state)
    {
        if (!pass.Uses.Any(u => u.Resource.SameVersion(texture) && u.TextureState == state))
        {
            throw new InvalidOperationException("Texture command state differs from its declared use.");
        }
    }
}
