namespace Lumyte.Composition;

/// <summary>Receives a passive generated construction recipe without subscribing or connecting the target.</summary>
public interface ICompositionRebuildable
{
    /// <summary>Stores a factory that creates a fresh declaration with the original arguments and actions.</summary>
    /// <param name="factory">The unconnected declaration factory.</param>
    void SetCompositionFactory(Func<object> factory);
}
