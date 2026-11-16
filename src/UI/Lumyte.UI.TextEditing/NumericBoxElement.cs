using System.Globalization;
using Lumyte.Input.Abstractions;
using Lumyte.TextEditing;
using EditorWidget = Lumyte.UI.TextEditing.Compose.Definitions.TextBox;
using NumericWidget = Lumyte.UI.TextEditing.Compose.Definitions.NumericBox;

namespace Lumyte.UI.TextEditing;

/// <summary>Separates the actual editable document from the last valid numeric value.</summary>
/// <param name="widget">The passive numeric declaration.</param>
public sealed class NumericBoxElement(NumericWidget widget) : TextBoxElement(widget)
{
    private bool _dirty;
    private bool _adopting;

    /// <summary>Gets the last accepted finite numeric value.</summary>
    public double? Value { get; private set; } = widget.Value;

    /// <summary>Gets a value indicating whether the edit buffer is a complete valid number or empty.</summary>
    public bool IsValid { get; private set; } = true;

    /// <inheritdoc/>
    protected override string DeclaredText => Format(Configuration.Value);

    /// <inheritdoc/>
    protected override UiNumericState? NumericState => new(Configuration.Min, Configuration.Max, null, Value, null, null, IsValid);

    private NumericWidget Configuration => (NumericWidget)Widget;

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(EditorWidget next, UiElementUpdateContext context)
    {
        if (ReferenceEquals(next, Widget))
        {
            return null;
        }

        context.CancelInput();
        double? value = ((NumericWidget)next).Value;
        return () =>
        {
            Value = value;
            IsValid = true;
            _dirty = false;
            _adopting = true;
        };
    }

    /// <inheritdoc/>
    protected override void OnWidgetUpdated(EditorWidget previous)
    {
        base.OnWidgetUpdated(previous);
        _adopting = false;
    }

    /// <inheritdoc/>
    protected override void OnCommittedTextChanged(UiTextChange change)
    {
        NumericWidget declaration = Configuration;
        if (Document.CaptureSnapshot().Composition is null)
        {
            AcceptText(change.Text);
        }

        if (IsMounted && ReferenceEquals(declaration, Widget) && Document.CaptureSnapshot().Revision == change.Revision)
        {
            base.OnCommittedTextChanged(change);
        }
    }

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (!_adopting)
        {
            SynchronizeText();
        }

        TextDocumentSnapshot snapshot = Document.CaptureSnapshot();
        if (snapshot.Composition is not null && context.Input is UiKeyInput { Key: Key.Enter })
        {
            return UiInputResult.Handled;
        }

        if (snapshot.Composition is null && !Widget.IsReadOnly)
        {
            if (context.Input is UiKeyInput { Key: Key.Escape, IsDown: true })
            {
                Restore();
                return UiInputResult.Handled;
            }

            if (context.Input is UiKeyInput { Key: Key.Enter, IsDown: true, IsRepeat: false } && context.IsFocused)
            {
                Complete();
                return UiInputResult.Handled;
            }

            if (context.Input is UiFocusChangedInput { IsFocused: false } && CanComplete())
            {
                Complete();
            }
        }

        return base.OnInput(in context);
    }

    private bool CanComplete()
    {
        if (_adopting || Surface is null)
        {
            return false;
        }

        try
        {
            if (!ReferenceEquals(Surface.FindElement(AutomationId), this))
            {
                return false;
            }
        }
        catch (ObjectDisposedException)
        {
            // Surface disposal cancels focus before unmounting its elements.
            return false;
        }

        for (UiElement? node = this; node is not null; node = node.Parent)
        {
            if (!node.Widget.IsEnabled || !node.Widget.IsVisible || !node.Widget.HitTestVisible || !node.Widget.IsPresent)
            {
                return false;
            }
        }

        return true;
    }

    private void AcceptText(string text)
    {
        _dirty |= text != Format(Value);
        double? value = null;
        bool incomplete = text.EndsWith(Configuration.Culture.NumberFormat.NumberDecimalSeparator, StringComparison.Ordinal);
        IsValid = text.Length == 0 || (!incomplete && double.TryParse(text, NumberStyles.Float, Configuration.Culture, out double parsed) && double.IsFinite(parsed) && parsed >= Configuration.Min && parsed <= Configuration.Max);
        if (!IsValid)
        {
            return;
        }

        if (text.Length != 0)
        {
            value = double.Parse(text, NumberStyles.Float, Configuration.Culture);
        }

        if (value == Value)
        {
            return;
        }

        Value = value;
        Configuration.OnChanged?.Invoke(value);
    }

    private void Complete()
    {
        TextDocumentSnapshot snapshot = Document.CaptureSnapshot();
        NumericWidget declaration = Configuration;
        AcceptText(snapshot.CommittedText);
        if (!IsMounted || !ReferenceEquals(declaration, Widget) || Document.CaptureSnapshot().Revision != snapshot.Revision || !IsValid || !_dirty)
        {
            return;
        }

        _dirty = false;
        Restore();
        if (IsMounted)
        {
            Configuration.OnCommitted?.Invoke(Value);
        }
    }

    private string Format(double? value) => value?.ToString("R", Configuration.Culture) ?? string.Empty;

    private void Restore()
    {
        TextDocumentSnapshot snapshot = Document.CaptureSnapshot();
        if (snapshot.Composition is not null)
        {
            return;
        }

        string text = Format(Value);
        ReplaceApplicationText(text);
        IsValid = true;
        _dirty = false;
        DocumentChanged();
    }
}
