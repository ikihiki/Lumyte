using Lumyte.Pipelines;

namespace Lumyte.Graphics.Pipelines;

/// <summary>Adds ordinary explicit transfer and independent-use stages.</summary>
public static class Transfers
{
    /// <summary>Creates an explicit host-to-device transfer declaration.</summary>
    /// <typeparam name="TSchema">The canonical schema.</typeparam>
    /// <returns>The immutable upload declaration.</returns>
    public static Upload<TSchema> Upload<TSchema>()
        where TSchema : ISchema
        => new();

    /// <summary>Creates an explicit host-to-device transfer with fixed source retention eligibility.</summary>
    /// <typeparam name="TSchema">The canonical schema.</typeparam>
    /// <param name="retention">Source eligibility or fresh invocation-derived storage.</param>
    /// <returns>The immutable upload declaration.</returns>
    public static Upload<TSchema> Upload<TSchema>(UploadRetention retention)
        where TSchema : ISchema
        => new(retention);

    /// <summary>Creates an explicit device-to-host transfer declaration.</summary>
    /// <typeparam name="TSchema">The canonical schema.</typeparam>
    /// <returns>The immutable download declaration.</returns>
    public static Download<TSchema> Download<TSchema>()
        where TSchema : ISchema
        => new();

    /// <summary>Creates an independent device use without copying or submitting.</summary>
    /// <typeparam name="TSchema">The canonical schema.</typeparam>
    /// <returns>The immutable passthrough declaration.</returns>
    public static DevicePassthrough<TSchema> Passthrough<TSchema>()
        where TSchema : ISchema
        => new();
}
