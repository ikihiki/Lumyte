using System.Runtime.CompilerServices;

namespace Lumyte.Pipelines;

/// <summary>Finite requirement helpers that do not add or execute stages.</summary>
public static class Requirements
{
    /// <summary>Conservative owned metadata bytes reserved for each published value lease.</summary>
    public const long ValueLeaseBytes = 64;

    /// <summary>Conservative metadata bytes reserved for one canonical data owner and typed storage.</summary>
    public const long SchemaDataBytes = 128;

    /// <summary>Reserves an independent host forwarding lease and a finite canonical input ceiling.</summary>
    /// <typeparam name="TSchema">The canonical schema.</typeparam>
    /// <param name="maxElements">The nonnegative element ceiling.</param>
    /// <returns>The finite host requirements.</returns>
    public static StageRequirements HostPassthrough<TSchema>(long maxElements)
        where TSchema : ISchema
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxElements);
        return new StageRequirements(checked(ValueLeaseBytes + Unsafe.SizeOf<Host<TSchema>>()), [])
        {
            HostInputSchema = typeof(TSchema),
            MaxHostInputElements = maxElements,
            OutputMaxElements = maxElements,
        };
    }
}
