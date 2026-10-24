namespace Lumyte.Resources;

/// <summary>Declares the execution environment required by a stage.</summary>
/// <typeparam name="TRuntime">The environment contract checked when the manager is constructed.</typeparam>
public interface IResourceStage<TRuntime> : IResourceStage
    where TRuntime : class, IResourceRuntime
{
    /// <inheritdoc/>
    Type IResourceStage.RuntimeType => typeof(TRuntime);
}
