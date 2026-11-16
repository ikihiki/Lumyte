namespace Lumyte.UI;

/// <summary>Implements persistent button behavior independently of replaceable declarations.</summary>
public partial class ButtonElement
{
    private readonly UiActivationRecognizer _activation = new();

    /// <summary>Gets a value indicating whether the current pointer is inside or a key is held.</summary>
    public bool IsPressed => _activation.IsPressed;

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context) =>
        _activation.HandleInput(in context, context.Input is UiPointerInput { Phase: UiPointerPhase.Down } && context.PointerPosition is { } position ? OnProbePointerDown(position) : UiInputResult.Unhandled, Activate);

    /// <inheritdoc/>
    protected override void OnPointerCaptureChanged(UiPointerCaptureState state) => _activation.PointerCaptureChanged(state);

    /// <inheritdoc/>
    protected override IEnumerable<UiElement> GetInputChildren() => GetChildren(Widget.Children);

    private void Activate()
    {
        Action? callback = Widget.OnClick;
        callback?.Invoke();
    }
}
