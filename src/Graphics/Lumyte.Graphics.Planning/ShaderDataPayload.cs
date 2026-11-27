using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class ShaderDataPayload<T>(ShaderDataPayload<T>? previous, PlanRange range, IReadOnlyList<IArgumentPiece>[] values, IReadOnlyList<PlanResourceUse>? copiedReferences = null)
    where T : struct, IShaderArguments
{
    internal ShaderDataPayload<T>? Previous { get; } = previous;

    internal PlanRange Range { get; } = range;

    internal IReadOnlyList<IArgumentPiece>[] Values { get; } = values;

    internal IReadOnlyList<PlanResourceUse> CopiedReferences { get; } = copiedReferences ?? [];

    internal IReadOnlyList<PlanResourceUse> ReferenceUses
    {
        get
        {
            var uses = new List<PlanResourceUse>();
            for (ShaderDataPayload<T>? current = this; current is not null; current = current.Previous)
            {
                uses.AddRange(current.CopiedReferences);
                foreach (IArgumentPiece piece in current.Values.SelectMany(v => v))
                {
                    uses.AddRange(piece.Dependencies);
                }
            }

            return uses.Distinct().ToArray();
        }
    }
}
