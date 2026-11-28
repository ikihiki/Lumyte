namespace Lumyte.Pipelines;

/// <summary>A captured typed stage declaration.</summary>
/// <typeparam name="TA">The TA port payload.</typeparam>
/// <typeparam name="TB">The TB port payload.</typeparam>
/// <typeparam name="TC">The TC port payload.</typeparam>
/// <typeparam name="TO">The TO port payload.</typeparam>
public interface IStage<TA, TB, TC, TO>
{
    /// <summary>Gets the immutable processor factory.</summary>
    IStageFactory<TA, TB, TC, TO> Factory { get; }
}

/// <summary>Creates a processor after requirements validation.</summary>
/// <typeparam name="TA">The TA port payload.</typeparam>
/// <typeparam name="TB">The TB port payload.</typeparam>
/// <typeparam name="TC">The TC port payload.</typeparam>
/// <typeparam name="TO">The TO port payload.</typeparam>
public interface IStageFactory<TA, TB, TC, TO> : IStageFactory
{
    /// <summary>Creates an independently owned processor.</summary>
    /// <param name="context">The explicit build capabilities.</param>
    /// <returns>The processor.</returns>
    IStageProcessor<TA, TB, TC, TO> Create(StageBuildContext context);
}

/// <summary>Prepares typed evaluations; implementations must support concurrent invocations.</summary>
/// <remarks>Processors are shared across retained runs. Keep invocation state in locals, not processor fields.</remarks>
/// <typeparam name="TA">The TA port payload.</typeparam>
/// <typeparam name="TB">The TB port payload.</typeparam>
/// <typeparam name="TC">The TC port payload.</typeparam>
/// <typeparam name="TO">The TO port payload.</typeparam>
public interface IStageProcessor<TA, TB, TC, TO> : IDisposable
{
    /// <summary>Evaluates the inputs without transferring their ownership.</summary>
    /// <param name="context">The per-invocation publication scope.</param>
    /// <param name="a">The first borrowed stage input.</param>
    /// <param name="b">The second borrowed stage input.</param>
    /// <param name="c">The third borrowed stage input.</param>
    /// <param name="cancellationToken">Cooperative cancellation.</param>
    /// <returns>An output lease belonging to this invocation.</returns>
    ValueTask<ValueLease<TO>> PrepareAsync(StagePreparationContext context, ValueLease<TA> a, ValueLease<TB> b, ValueLease<TC> c, CancellationToken cancellationToken);
}
