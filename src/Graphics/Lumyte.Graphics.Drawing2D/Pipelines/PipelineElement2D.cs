namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Independent custom processing state; passive declarations never own these fields.</summary>
public abstract class PipelineElement2D
{
    private readonly object _adoptionGate = new();

    /// <summary>Gets the passive configuration first adopted by this processing instance.</summary>
    public PipelineDeclaration2D? Declaration { get; private set; }

    private object? BuildIdentity { get; set; }

    internal void Adopt(PipelineDeclaration2D declaration, object buildIdentity)
    {
        lock (_adoptionGate)
        {
            if (BuildIdentity is not null && !ReferenceEquals(BuildIdentity, buildIdentity))
            {
                throw new InvalidOperationException("CreateElement must return a fresh processor, not a processor already adopted by another build.");
            }

            Declaration ??= declaration;
            BuildIdentity = buildIdentity;
        }
    }

    internal void ReleaseReservation(object buildIdentity)
    {
        lock (_adoptionGate)
        {
            if (ReferenceEquals(BuildIdentity, buildIdentity))
            {
                BuildIdentity = null;
                Declaration = null;
            }
        }
    }
}
