using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal static class ExtendedCommandValidation
{
    internal static void Kernel(PlanPass pass, GraphicsDeviceSession session)
    {
        pass.Owner.Mutable();
        session.Validate();
        if (session != pass.Owner.Frame.Session)
        {
            throw new ArgumentException("Foreign kernel.");
        }
    }

    internal static void Read(PlanPass pass, PlanResource resource, PlanRange range, PipelineStage stage, ResourceAccess access)
    {
        pass.Owner.Validate(resource, range);
        Require(pass, resource, range, false, stage, access, null);
    }

    internal static void Indirect(PlanPass pass, PlanResource resource, PlanRange range)
    {
        if (range.Count != 1)
        {
            throw new ArgumentOutOfRangeException(nameof(range), "Exactly one indirect record is required.");
        }

        Read(pass, resource, range, PipelineStage.DrawIndirect, ResourceAccess.IndirectRead);
    }

    internal static void Index(PlanPass pass, PlanResource resource, PlanRange range, uint count, uint firstIndex)
    {
        if (count == 0 || (ulong)firstIndex + count > range.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Read(pass, resource, range, PipelineStage.IndexInput, ResourceAccess.IndexRead);
    }

    internal static void DrawCounts(uint count, uint instances)
    {
        if (count == 0 || instances == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }
    }

    internal static void Counts(uint x, uint y, uint z = 1)
    {
        if (x == 0 || y == 0 || z == 0 || (ulong)x * y > uint.MaxValue || (ulong)x * y * z > uint.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }
    }

    internal static void Raster(PlanPass pass, RasterAttachments attachments, CompleteRasterState state)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(state);
        if (attachments.Colors.Count == 0 && attachments.DepthStencil is null)
        {
            throw new ArgumentException("At least one attachment is required.");
        }

        ArgumentNullException.ThrowIfNull(state.Raster);
        ArgumentNullException.ThrowIfNull(state.Raster.RenderState);
        Viewport viewport = state.Raster.Viewport;
        ScissorRect scissor = state.Raster.Scissor;
        if (!float.IsFinite(viewport.X) || !float.IsFinite(viewport.Y) || !float.IsFinite(viewport.Width) || !float.IsFinite(viewport.Height) || viewport.Width <= 0 || viewport.Height <= 0 || !float.IsFinite(viewport.MinDepth) || !float.IsFinite(viewport.MaxDepth) || viewport.MinDepth < 0 || viewport.MaxDepth > 1 || viewport.MinDepth > viewport.MaxDepth || scissor.Width == 0 || scissor.Height == 0)
        {
            throw new ArgumentException("Invalid viewport or scissor.");
        }

        if (state.StencilReference > 255 || !float.IsFinite(state.BlendConstant.Red) || !float.IsFinite(state.BlendConstant.Green) || !float.IsFinite(state.BlendConstant.Blue) || !float.IsFinite(state.BlendConstant.Alpha) || state.Raster.RenderState.ColorTargets.Count != attachments.Colors.Count)
        {
            throw new ArgumentException("Invalid complete raster state.");
        }

        var identities = new HashSet<ResourceIdentity>();
        foreach (RasterColorAttachment color in attachments.Colors)
        {
            Attachment(pass, color.Texture, color.Load == AttachmentLoadOp.Load, PipelineStage.ColorOutput, ResourceAccess.ColorWrite, ResourceAccess.ColorRead, TextureState.ColorAttachment, identities);
        }

        if (attachments.DepthStencil is { } depth)
        {
            if (!float.IsFinite(depth.DepthClear) || depth.DepthClear < 0 || depth.DepthClear > 1 || depth.StencilClear > 255)
            {
                throw new ArgumentException("Invalid depth/stencil clear values.");
            }

            Attachment(pass, depth.Texture, depth.DepthLoad == AttachmentLoadOp.Load || depth.StencilLoad == AttachmentLoadOp.Load, PipelineStage.DepthStencil, ResourceAccess.DepthStencilWrite, ResourceAccess.DepthStencilRead, TextureState.DepthStencilAttachment, identities);
        }
        else if (state.Raster.RenderState.DepthStencil.DepthTestEnable || state.Raster.RenderState.DepthStencil.DepthWriteEnable || state.Raster.RenderState.DepthStencil.StencilTestEnable)
        {
            throw new ArgumentException("Depth/stencil state requires an attachment.");
        }
    }

    private static void Attachment(PlanPass pass, PlanTexture texture, bool load, PipelineStage stage, ResourceAccess write, ResourceAccess read, TextureState state, HashSet<ResourceIdentity> identities)
    {
        pass.Owner.Validate(texture, texture.Range);
        if (!identities.Add(texture.Identity) || pass.UseList.Any(u => u.Resource.Identity == texture.Identity && u.TextureState == TextureState.Sampled))
        {
            throw new NotSupportedException("Attachment aliasing and texture feedback are not supported.");
        }

        Require(pass, texture, texture.Range, true, stage, write, state);
        if (load)
        {
            Require(pass, texture, texture.Range, false, stage, read, state);
        }
    }

    private static void Require(PlanPass pass, PlanResource resource, PlanRange range, bool write, PipelineStage stage, ResourceAccess access, TextureState? state)
    {
        if (!pass.UseList.Any(u => u.Resource.SameVersion(resource) && u.Range.Contains(range) && (write ? u.Access != PlanAccess.Read : u.Access != PlanAccess.Write) && (u.Scope.Stages & stage) == stage && (u.Scope.Access & access) == access && (state is null || u.TextureState == state)))
        {
            throw new InvalidOperationException("Command requires a matching predeclared range, stage, access and texture state.");
        }
    }
}
