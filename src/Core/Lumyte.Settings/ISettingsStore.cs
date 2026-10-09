namespace Lumyte.Settings;

/// <summary>A borrowed storage medium with atomic replacement semantics.</summary>
public interface ISettingsStore
{
    /// <summary>Reads the document; null means it does not exist. Storage failures use IOException.</summary>
    /// <param name="cancellationToken">Cancels reading.</param>
    /// <returns>The document bytes, or null.</returns>
    ValueTask<byte[]?> ReadAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the document atomically; failures preserve the old document.</summary>
    /// <param name="data">The replacement bytes.</param>
    /// <param name="cancellationToken">Cancels before committing; cancellation after commit is ignored.</param>
    /// <returns>Completion after the storage transaction commits.</returns>
    ValueTask WriteAtomicallyAsync(ReadOnlyMemory<byte> data, CancellationToken cancellationToken = default);
}
