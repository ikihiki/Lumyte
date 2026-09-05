namespace Lumyte.Input;

public interface ITextInputContext
{
    bool IsAvailable { get; }

    bool IsActive { get; }

    void Activate(ITextInputClient client);

    void Deactivate();

    void NotifyTextChanged(TextChange change);

    void NotifySelectionChanged();

    void NotifyLayoutChanged();
}
