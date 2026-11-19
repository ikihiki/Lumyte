namespace Lumyte.UI;

/// <summary>An actual model-owning item viewport, independent of semantic role labels.</summary>
public interface IUiItemViewport
{
    /// <summary>Reveals a model key on the owner thread without constructing preceding items.</summary>
    /// <param name="key">The ordinal model key.</param>
    /// <returns>Whether the key can be revealed.</returns>
    bool BringIntoView(string key);
}
