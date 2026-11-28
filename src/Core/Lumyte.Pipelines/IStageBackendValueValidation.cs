namespace Lumyte.Pipelines;

/// <summary>Validates every generic backend declaration access before it can escape the bounded scope.</summary>
/// <remarks>Backend capabilities must enforce their resource/frame provenance and register producer dependencies here, including every opaque child of supported typed bundles. These hooks are the common guard for all public backend scope paths; do not rely only on backend-specific facade methods. During either hook, Core rejects publication, quota/allocation/ownership changes, host placement creation and recursive backend value validation on the same invocation. Read-only CPU input access remains subject to the ordinary CPU policy. Backend-specific dependency metadata may be recorded. Guard state is released in a finally block, so a rejected hook consumes no output slot and later legal publication remains possible.</remarks>
public interface IStageBackendValueValidation
{
    /// <summary>Validates an admitted immutable input declaration and records its backend producer dependencies.</summary>
    /// <typeparam name="T">The exact declaration payload type.</typeparam>
    /// <param name="value">The admitted input declaration, possibly a recursive bundle.</param>
    void ValidateRead<T>(T value);

    /// <summary>Validates a declaration before any output quota is charged or lease is created.</summary>
    /// <typeparam name="T">The exact declaration payload type.</typeparam>
    /// <param name="value">The prospective immutable output declaration, possibly a recursive bundle.</param>
    void ValidatePublication<T>(T value);
}
