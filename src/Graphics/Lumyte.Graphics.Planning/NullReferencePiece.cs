using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class NullReferencePiece<T> : IArgumentPiece
{
    /// <summary>Declares prepare.</summary>
    /// <param name = "reservation">The reservation.</param>
    /// <param name = "resources">The resources.</param>
    public void Prepare(ArgumentTableReservation reservation, PreparedResources resources)
    {
    }

    /// <summary>Declares write.</summary>
    /// <param name = "writer">The writer.</param>
    /// <typeparam name = "TWriter">The typed shader or resource contract.</typeparam>
    public void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct => writer.WriteReference<T>(null);
}
