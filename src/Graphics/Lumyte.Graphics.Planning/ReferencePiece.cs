using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class ReferencePiece<T>(Action<ArgumentTableReservation, PreparedResources, Action<IGpuRef<T>>> register, IReadOnlyList<PlanResourceUse>? dependencies = null) : IArgumentPiece
{
    private IGpuRef<T>? _resolved;

    /// <inheritdoc/>
    public IReadOnlyList<PlanResourceUse> Dependencies { get; } = dependencies ?? [];

    /// <summary>Gets a value indicating whether gets has reference.</summary>
    public bool HasReference => true;

    /// <summary>Declares prepare.</summary>
    /// <param name = "reservation">The reservation.</param>
    /// <param name = "resources">The resources.</param>
    public void Prepare(ArgumentTableReservation reservation, PreparedResources resources) => register(reservation, resources, value => _resolved = value);

    /// <summary>Declares write.</summary>
    /// <param name = "writer">The writer.</param>
    /// <typeparam name = "TWriter">The typed shader or resource contract.</typeparam>
    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct => writer.WriteReference(_resolved ?? throw new InvalidOperationException("Arguments have not been patched."));
}
