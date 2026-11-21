using System.Numerics;
using Lumyte.Pipelines;

namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>An immutable, preparation-local snapshot. Native references are never canonical wire elements.</summary>
public sealed class RenderRequest2D : IImmutablePipelineInput
{
    internal RenderRequest2D(RenderPreparationContext2D context, IReadOnlyList<DrawingCommand2D> commands, Action<long>? reserve = null)
        : this(context.Target, commands, Guid.NewGuid(), reserve)
    {
        Context = context;
    }

    internal RenderRequest2D(DrawingTargetInfo2D target, IReadOnlyList<DrawingCommand2D> commands, Guid sourceIdentity, Action<long>? reserve = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(commands);
        if (sourceIdentity == Guid.Empty)
        {
            throw new ArgumentException("A source identity is required.", nameof(sourceIdentity));
        }

        if (target.Description.Width == 0 || target.Description.Height == 0 || !float.IsFinite(target.PixelScale) || target.PixelScale <= 0 || !float.IsFinite(target.PixelOrigin.X) || !float.IsFinite(target.PixelOrigin.Y))
        {
            throw new ArgumentException("Invalid frame grid.", nameof(target));
        }

        Target = target with { };
        reserve?.Invoke(checked(256 + ((long)commands.Count * 16)));
        Commands = Array.AsReadOnly(commands.Select(x => x ?? throw new ArgumentException("Null command.", nameof(commands))).ToArray());
        SourceIdentity = sourceIdentity;
        ResourceSnapshot = new(Commands, reserve);
    }

    /// <summary>Gets the immutable actual-input counts and frame identity used for allocation admission.</summary>
    public SchemaExtent Description => new(Commands.Count, Metadata());

    /// <summary>Gets the exact source identity shared by all projected fields.</summary>
    public Guid SourceIdentity { get; }

    /// <summary>Gets the captured immutable contiguous logical commands, including invisible commands.</summary>
    public IReadOnlyList<DrawingCommand2D> Commands { get; }

    /// <summary>Gets the captured target geometry (no native allocation).</summary>
    public DrawingTargetInfo2D Target { get; }

    internal RenderPreparationContext2D? Context { get; }

    internal RenderResourcesSnapshot2D ResourceSnapshot { get; }

    internal Dictionary<string, object> Metadata() => new()
    {
        ["source"] = SourceIdentity,
        ["width"] = Target.Description.Width,
        ["height"] = Target.Description.Height,
        ["samples"] = 16,
        ["gridVersion"] = 1,
        ["commandCount"] = Commands.Count,
        ["viewCount"] = ResourceSnapshot.Views.Count,
        ["samplerCount"] = ResourceSnapshot.Samplers.Count,
    };

    internal SchemaExtent Extent(long count, bool snapshot = false)
    {
        Dictionary<string, object> m = Metadata();
        if (snapshot)
        {
            m.Add("commandsSnapshot", this);
        }

        if (snapshot)
        {
            m.Add("resourceSnapshot", ResourceSnapshot);
        }

        return new(count, m);
    }

    internal Vector4[] FrameCells() => [new(Target.Description.Width, Target.Description.Height, Target.PixelScale, 1), new(Target.PixelOrigin, 16, 1)];
}
