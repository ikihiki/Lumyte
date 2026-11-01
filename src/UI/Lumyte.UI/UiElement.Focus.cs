namespace Lumyte.UI;

/// <summary>A persistent UI instance with independent configuration, topology and lifetime.</summary>
public partial class UiElement
{
    internal bool InputFocusable { get; private set; }

    internal void CollectFocusCandidates(List<UiElement> candidates, object version)
    {
        if (!ReferenceEquals(InputVersion, version))
        {
            return;
        }

        if (InputFocusable)
        {
            candidates.Add(this);
        }

        for (int index = 0; index < _inputChildren.Count; index++)
        {
            _inputChildren[index].CollectFocusCandidates(candidates, version);
        }
    }
}
