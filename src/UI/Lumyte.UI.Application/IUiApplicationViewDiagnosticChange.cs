namespace Lumyte.UI.Application;

/// <summary>Coordinates one private diagnostic target reservation.</summary>
internal interface IUiApplicationViewDiagnosticChange : IAsyncDisposable
{
    /// <summary>Gets the reserved target identity.</summary>
    Guid? TargetId { get; }

    /// <summary>Gets a value indicating whether the reserved target may still be published.</summary>
    bool IsReady { get; }

    /// <summary>Stops new jobs and drains the old group.</summary>
    /// <param name="origin">The trusted input job cause.</param>
    /// <param name="cancellationToken">Cancels only this wait.</param>
    /// <returns>The quiescence completion.</returns>
    ValueTask QuiesceAsync(Guid? origin, CancellationToken cancellationToken);

    /// <summary>Publishes the prepared reservation synchronously.</summary>
    void Commit();

    /// <summary>Discards a candidate and resumes a live old target.</summary>
    /// <param name="cancellationToken">Cancels only this wait.</param>
    /// <returns>The discard completion.</returns>
    ValueTask DiscardAsync(CancellationToken cancellationToken);
}
