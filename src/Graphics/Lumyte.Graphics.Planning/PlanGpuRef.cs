using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Declares plan gpu ref.</summary>
/// <typeparam name = "T">The typed shader or resource contract.</typeparam>
public sealed class PlanGpuRef<T> : IGpuRef<T>, IPlanReference
{
    private readonly Action<ArgumentTableReservation, PreparedResources, Action<IGpuRef<T>>> _register;

    internal PlanGpuRef(PlanPass pass, PlanResource? resource, ulong count, Action<ArgumentTableReservation, PreparedResources, Action<IGpuRef<T>>> register)
    {
        (Pass, Resource, Count, _register) = (pass, resource, count, register);
    }

    /// <summary>Gets count.</summary>
    public ulong Count { get; }

    internal PlanPass Pass { get; }

    internal PlanResource? Resource { get; }

    /// <summary>Declares get element.</summary>
    /// <param name = "index">The index.</param>
    /// <returns>The declared result.</returns>
    public IGpuRef<T> GetElement(ulong index)
    {
        if (index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new PlanGpuRef<T>(Pass, Resource, 1, (r, p, done) => _register(r, p, native => done(native.GetElement(index))));
    }

    /// <inheritdoc/>
    IArgumentPiece IPlanReference.Capture(PlanPass pass)
    {
        if (pass != Pass)
        {
            throw new ArgumentException("A symbolic reference belongs to one operation scope.");
        }

        return new ReferencePiece<T>(_register, CapturedDependencies());
    }

    /// <inheritdoc/>
    IArgumentPiece IPlanReference.CaptureForData(PlanPass pass)
    {
        if (pass.Owner != Pass.Owner)
        {
            throw new ArgumentException("A shader-data reference belongs to another frame.");
        }

        return new ReferencePiece<T>(_register, CapturedDependencies());
    }

    private IReadOnlyList<PlanResourceUse> CapturedDependencies()
        => Resource is null ? [] : Pass.Uses.Where(u => u.Resource.SameVersion(Resource) && u.Range.Overlaps(Resource.Range)).Select(u => u with { Resource = Resource, Range = new(Math.Max(u.Range.Offset, Resource.Range.Offset), Math.Min(u.Range.End, Resource.Range.End) - Math.Max(u.Range.Offset, Resource.Range.Offset)) }).ToArray();
}
