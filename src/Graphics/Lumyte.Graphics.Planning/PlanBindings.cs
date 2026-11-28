using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares plan bindings.</summary>
public static class PlanBindings
{
    /// <summary>Declares buffer.</summary>
    /// <param name = "pass">The pass.</param>
    /// <param name = "buffer">The buffer.</param>
    /// <param name = "range">The range.</param>
    /// <param name = "access">The access.</param>
    /// <typeparam name = "T">The typed shader or resource contract.</typeparam>
    /// <returns>The declared result.</returns>
    public static PlanGpuRef<T> Buffer<T>(this PlanPass pass, PlanBuffer<T> buffer, PlanRange range, ArgumentBufferAccess access = ArgumentBufferAccess.Read)
        where T : unmanaged
    {
        pass.Owner.Mutable();
        if (access is not (ArgumentBufferAccess.Read or ArgumentBufferAccess.Write or ArgumentBufferAccess.ReadWrite))
        {
            throw new ArgumentOutOfRangeException(nameof(access));
        }

        ResourceAccess required = ((access & ArgumentBufferAccess.Read) != 0 ? ResourceAccess.ShaderRead : ResourceAccess.None) | ((access & ArgumentBufferAccess.Write) != 0 ? ResourceAccess.ShaderWrite : ResourceAccess.None);
        if (!pass.Uses.Any(u => u.Resource.SameVersion(buffer) && u.Range.Contains(range) && (u.Scope.Access & required) == required && (u.Scope.Stages & (PipelineStage.AllGraphics | PipelineStage.ComputeShader)) != 0))
        {
            throw new InvalidOperationException("Shader binding requires matching shader stage/access declaration.");
        }

        pass.Require(buffer, range, (access & ArgumentBufferAccess.Write) != 0);
        if ((access & ArgumentBufferAccess.Read) != 0)
        {
            pass.Require(buffer, range, false);
        }

        return new(pass, buffer, range.Count, (reservation, resources, done) => reservation.RegisterBuffer(() => resources.Buffer(buffer, range), done, access));
    }

    /// <summary>Declares sample.</summary>
    /// <param name = "pass">The pass.</param>
    /// <param name = "texture">The texture.</param>
    /// <returns>The declared result.</returns>
    public static PlanGpuRef<IGraphicsTextureView> Sample(this PlanPass pass, PlanTexture texture)
    {
        pass.Owner.Mutable();
        pass.Require(texture, texture.Range, false);
        StructuredTextureCommands.RequireState(pass, texture, TextureState.Sampled);
        if (!pass.Uses.Any(u => u.Resource.SameVersion(texture) && (u.Scope.Access & ResourceAccess.ShaderRead) != 0))
        {
            throw new InvalidOperationException("Texture sampling requires declared shader read access.");
        }

        return new(pass, texture, 1, (reservation, resources, done) => reservation.RegisterTexture(() => resources.Texture(texture), done));
    }

    /// <summary>Declares a session-owned sampler reference for one structured shader operation.</summary>
    /// <param name = "pass">The declaring operation scope.</param>
    /// <param name = "sampler">The independently retained sampler handle.</param>
    /// <returns>The typed symbolic sampler reference.</returns>
    public static PlanGpuRef<IGraphicsSampler> Sampler(this PlanPass pass, PlanSampler sampler)
    {
        pass.Owner.Mutable();
        if (sampler.Session != pass.Owner.Frame.Session)
        {
            throw new ArgumentException("Foreign sampler authority.");
        }

        return new(pass, null, 1, (reservation, resources, done) => reservation.RegisterSampler(sampler.Sampler, done));
    }
}
