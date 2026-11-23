namespace Lumyte.Pipelines;

/// <summary>TA captured typed stage declaration.</summary>
/// <typeparam name="TA">The TA port payload.</typeparam>
/// <typeparam name="TO">The TO port payload.</typeparam>
public interface IStage<TA, TO>
{
    /// <summary>Gets the immutable processor factory.</summary>
    IStageFactory<TA, TO> Factory { get; }
}

/// <summary>Creates a processor after requirements validation.</summary>
/// <typeparam name="TA">The TA port payload.</typeparam>
/// <typeparam name="TO">The TO port payload.</typeparam>
public interface IStageFactory<TA, TO> : IStageFactory
{
    /// <summary>Creates an independently owned processor.</summary>
    /// <param name="context">The explicit build capabilities.</param>
    /// <returns>The processor.</returns>
    IStageProcessor<TA, TO> Create(StageBuildContext context);
}

/// <summary>Prepares one typed stage evaluation.</summary>
/// <typeparam name="TA">The TA port payload.</typeparam>
/// <typeparam name="TO">The TO port payload.</typeparam>
public interface IStageProcessor<TA, TO> : IDisposable
{
    /// <summary>Evaluates the inputs without transferring their ownership.</summary>
    /// <param name="context">The per-invocation publication scope.</param>
    /// <param name="a">The first borrowed stage input.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>An output lease belonging to this invocation.</returns>
    ValueTask<ValueLease<TO>> PrepareAsync(StagePreparationContext context, ValueLease<TA> a, CancellationToken cancellationToken);
}
