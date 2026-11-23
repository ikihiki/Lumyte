namespace Lumyte.UI;

/// <summary>Connects ordinary content close controls to a borrowed presentation owner.</summary>
public sealed partial class UiSurface
{
    internal Action? CloseRequested { get; set; }

    internal void RequestClose() => CloseRequested?.Invoke();
}
