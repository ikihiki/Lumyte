using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

/// <summary>Replays one immutable numeric field or typed native-reference fixup.</summary>
internal interface IArgumentPiece
{
    /// <summary>Gets declaration-time resource uses reached by this typed reference field.</summary>
    IReadOnlyList<PlanResourceUse> Dependencies => [];

    /// <summary>Gets a value indicating whether gets has reference.</summary>
    bool HasReference => false;

    /// <summary>Declares prepare.</summary>
    /// <param name = "reservation">The reservation.</param>
    /// <param name = "resources">The resources.</param>
    void Prepare(ArgumentTableReservation reservation, PreparedResources resources);

    /// <summary>Declares write.</summary>
    /// <param name = "writer">The writer.</param>
    /// <typeparam name = "TWriter">The typed shader or resource contract.</typeparam>
    void Write<TWriter>(ref TWriter writer)
        where TWriter : IShaderValueWriter, allows ref struct;
}
