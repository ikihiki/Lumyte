using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using Lumyte.Mathematics.Colors;
using WidgetDefinition = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Controls;

/// <summary>Shares one capture owner and numeric track geometry without hidden widgets.</summary>
/// <typeparam name="TWidget">The passive declaration.</typeparam>
/// <param name="widget">The initial declaration.</param>
public abstract class SliderElementBase<TWidget>(TWidget widget) : UiElement<TWidget>(widget)
    where TWidget : WidgetDefinition
{
    private long? _pointer;
    private Key? _key;
    private bool _gesture;
    private float _down;
    private bool _dragging;
    private bool _thumbPressed;
    private float _anchor;

    /// <summary>Gets the lower bound.</summary>
    protected abstract double Minimum
    {
        get;
    }

    /// <summary>Gets the upper bound.</summary>
    protected abstract double Maximum
    {
        get;
    }

    /// <summary>Gets the positive step.</summary>
    protected abstract double Increment
    {
        get;
    }

    /// <summary>Gets the orientation.</summary>
    protected abstract UiOrientation Orientation
    {
        get;
    }

    /// <summary>Gets a value indicating whether visual direction is reversed.</summary>
    protected abstract bool Reversed
    {
        get;
    }

    /// <summary>Gets the track color.</summary>
    protected abstract SRgba<float> TrackColor
    {
        get;
    }

    /// <summary>Gets the thumb color.</summary>
    protected abstract SRgba<float> ThumbColor
    {
        get;
    }

    /// <summary>Gets the currently selected thumb value.</summary>
    protected abstract double ActiveValue
    {
        get;
    }

    /// <summary>Gets the numeric thumb positions.</summary>
    protected abstract IEnumerable<double> ThumbValues
    {
        get;
    }

    /// <summary>Gets a value indicating whether owned decorative content replaces the built-in thumb.</summary>
    protected virtual bool HasThumbVisual => false;

    /// <summary>Gets a value indicating whether owned content replaces the track.</summary>
    protected virtual bool HasTrackVisual => false;

    /// <inheritdoc/>
    protected override bool PreservePointerGesture => true;

    /// <inheritdoc/>
    protected override bool CacheRetainedDrawing => true;

    /// <inheritdoc/>
    protected override bool UsesDefaultRecordingOrder => !HasTrackVisual;

    /// <inheritdoc/>
    protected override bool HasAutomationActivePress => _gesture;

    /// <inheritdoc/>
    protected override IEnumerable<UiElement> GetInputChildren() => Array.Empty<UiElement>();

    /// <inheritdoc/>
    protected override IEnumerable<UiElement> GetAutomationChildren() => Array.Empty<UiElement>();

    /// <inheritdoc/>
    protected override UiInputResult OnProbePointerDown(Vector2 localPosition) => IsInsideInputBody(localPosition) ? UiInputResult.Handled : UiInputResult.StopPropagation;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 size = constraints.Constrain(Orientation == UiOrientation.Horizontal ? new(160, 24) : new(24, 160));
        foreach (UiElement child in Children)
        {
            LayoutChild(child, UiBoxConstraints.Tight(size));
            SetChildOffset(child, Vector2.Zero);
        }

        return size;
    }

    /// <summary>Gets whether a specific thumb is replaced.</summary>
    /// <param name="index">The lower-to-upper thumb index.</param>
    /// <returns>Whether owned content replaces this thumb.</returns>
    protected virtual bool HasThumbVisualAt(int index) => HasThumbVisual;

    /// <summary>Selects the thumb nearest a pointer, preserving deterministic ties.</summary>
    /// <param name="fraction">The numeric track fraction.</param>
    protected virtual void SelectThumb(double fraction)
    {
    }

    /// <summary>Selects the other thumb for keyboard input.</summary>
    protected virtual void SelectOtherThumb()
    {
    }

    /// <summary>Accepts an already snapped user value before notification.</summary>
    /// <param name="value">The requested value.</param>
    protected abstract void Accept(double value);

    /// <summary>Notifies completion after input ownership ends.</summary>
    protected abstract void Committed();

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.Input is UiFocusChangedInput { IsFocused: false } or UiKeyCancelInput)
        {
            _gesture = false;
            _key = null;
            return UiInputResult.Unhandled;
        }

        if (context.Input is UiPointerInput pointer)
        {
            float axis = Orientation == UiOrientation.Horizontal ? context.PointerPosition!.Value.X : context.PointerPosition!.Value.Y;
            if (pointer.Phase == UiPointerPhase.Down && context.IsInside && !_gesture)
            {
                if (!context.CapturePointer())
                {
                    return UiInputResult.StopPropagation;
                }

                _pointer = pointer.PointerId;
                _gesture = true;
                _down = axis;
                _dragging = false;
                SelectThumb(Fraction(axis));
                float length = Orientation == UiOrientation.Horizontal ? ContentSize.X : ContentSize.Y;
                float extent = Math.Min(10, length);
                double fraction = SliderScale.Fraction(ActiveValue, Minimum, Maximum);
                if (Reversed)
                {
                    fraction = 1 - fraction;
                }

                float start = (float)fraction * Math.Max(0, length - extent);
                _thumbPressed = axis >= start && axis < start + extent;
                _anchor = _thumbPressed ? axis - ((float)fraction * length) : 0;
                context.RequestFocus();
                return UiInputResult.Handled;
            }

            if (context.IsCaptured)
            {
                // Surface releases capture before terminal delivery; context remains authoritative.
                if (pointer.Phase == UiPointerPhase.Cancel)
                {
                    _gesture = false;
                    return UiInputResult.Handled;
                }

                if (pointer.Phase == UiPointerPhase.Up)
                {
                    bool complete = _gesture;
                    _gesture = false;
                    _pointer = null;
                    if (complete)
                    {
                        TWidget declaration = Widget;
                        if (!_dragging && !_thumbPressed)
                        {
                            Accept(FromFraction(Fraction(axis - _anchor)));
                        }

                        if (IsMounted && !_gesture && ReferenceEquals(declaration, Widget))
                        {
                            Committed();
                        }
                    }

                    return UiInputResult.Handled;
                }

                if (pointer.Phase == UiPointerPhase.Move && _gesture)
                {
                    _dragging |= Math.Abs(axis - _down) >= 3;
                    if (_dragging)
                    {
                        Accept(FromFraction(Fraction(axis - _anchor)));
                    }

                    return UiInputResult.Handled;
                }
            }
        }

        if (context.Input is not UiKeyInput key)
        {
            return UiInputResult.Unhandled;
        }

        if (key.Key == Key.Escape && key.IsDown && _gesture)
        {
            _gesture = false;
            Key? ownedKey = _key;
            long? ownedPointer = _pointer;
            _key = null;
            _pointer = null;
            if (ownedKey is { } capturedKey)
            {
                context.ReleaseKeyCapture(capturedKey);
            }

            if (ownedPointer is { } capturedPointer)
            {
                context.ReleasePointerCapture(capturedPointer);
            }

            return UiInputResult.Handled;
        }

        if (key.Key == Key.Space && key.IsDown && !key.IsRepeat && context.IsFocused && !_gesture)
        {
            SelectOtherThumb();
            return UiInputResult.Handled;
        }

        if (!key.IsDown)
        {
            if (context.IsKeyCaptured && _key == key.Key && _gesture)
            {
                _key = null;
                _gesture = false;
                Committed();
                return UiInputResult.Handled;
            }

            return UiInputResult.Unhandled;
        }

        bool navigation = key.Key is Key.Home or Key.End or Key.PageUp or Key.PageDown || (Orientation == UiOrientation.Horizontal ? key.Key is Key.ArrowLeft or Key.ArrowRight : key.Key is Key.ArrowUp or Key.ArrowDown);
        if (!navigation || key.Modifiers != UiModifiers.None)
        {
            return UiInputResult.Unhandled;
        }

        if (!context.IsFocused || _pointer is not null || (_key is not null && _key != key.Key))
        {
            return UiInputResult.Unhandled;
        }

        if (key.IsRepeat ? !context.IsKeyCaptured : !context.CaptureKey())
        {
            return UiInputResult.Unhandled;
        }

        _key = key.Key;
        _gesture = true;
        bool positive = key.Key is Key.ArrowRight or Key.ArrowDown or Key.PageDown;
        double delta = Increment * (key.Key is Key.PageUp or Key.PageDown ? 10 : 1);
        if (positive == Reversed)
        {
            delta = -delta;
        }

        double value = key.Key switch
        {
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => Math.Clamp(ActiveValue + delta, Minimum, Maximum),
        };
        Accept(SliderScale.Snap(value, Minimum, Maximum, Increment));
        return UiInputResult.Handled;
    }

    /// <inheritdoc/>
    protected override void OnPointerCaptureChanged(UiPointerCaptureState state)
    {
        if (!state.IsCaptured && _pointer == state.PointerId)
        {
            _pointer = null;

            // Terminal Up is delivered after this notification. Cancel is delivered explicitly.
        }
    }

    /// <inheritdoc/>
    protected override void RecordOverride(DrawingCanvas2D canvas)
    {
        if (!HasTrackVisual)
        {
            base.RecordOverride(canvas);
            return;
        }

        RecordSelf(canvas);
        foreach (UiElement child in Children)
        {
            RecordChild(child, canvas);
        }

        RecordBuiltinThumbs(canvas);
    }

    /// <inheritdoc/>
    protected override void OnRecord(DrawingCanvas2D canvas)
    {
        if (!HasTrackVisual)
        {
            canvas.FillRect(new(0, 0, ContentSize.X, ContentSize.Y), TrackColor);
            RecordBuiltinThumbs(canvas);
        }
    }

    private void RecordBuiltinThumbs(DrawingCanvas2D canvas)
    {
        int index = 0;
        foreach (double value in ThumbValues)
        {
            if (HasThumbVisualAt(index++))
            {
                continue;
            }

            float position = (float)SliderScale.Fraction(value, Minimum, Maximum);
            if (Reversed)
            {
                position = 1 - position;
            }

            float length = Orientation == UiOrientation.Horizontal ? ContentSize.X : ContentSize.Y;
            float extent = Math.Min(10, length);
            float start = position * Math.Max(0, length - extent);
            canvas.FillRect(Orientation == UiOrientation.Horizontal ? new(start, 0, extent, ContentSize.Y) : new(0, start, ContentSize.X, extent), ThumbColor);
        }
    }

    private double Fraction(float position)
    {
        float length = Orientation == UiOrientation.Horizontal ? ContentSize.X : ContentSize.Y;
        double fraction = length <= 0 ? 0 : Math.Clamp((double)position / length, 0, 1);
        return Reversed ? 1 - fraction : fraction;
    }

    private double FromFraction(double fraction) => SliderScale.Snap(SliderScale.Interpolate(Minimum, Maximum, fraction), Minimum, Maximum, Increment);
}
