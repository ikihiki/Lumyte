using System.Numerics;

namespace Lumyte.Input.Processing;

/// <summary>Maps touch movement, swipes, pinch and rotation to logical controller controls.</summary>
public sealed class TouchControllerGenerator : IVirtualDeviceGenerator
{
    private readonly float _radius;
    private readonly float _swipeDistance;
    private readonly Dictionary<TouchContactId, Contact> _contacts = new();
    private bool _focused;
    private float? _distance;
    private float? _angle;

    /// <summary>Initializes a new instance of the <see cref = "TouchControllerGenerator"/> class.</summary>
    /// <param name = "radius">The radius value.</param>
    /// <param name = "swipeDistance">The swipeDistance value.</param>
    public TouchControllerGenerator(float radius = 100, float swipeDistance = 80)
    {
        if (!float.IsFinite(radius) || radius <= 0 || !float.IsFinite(swipeDistance) || swipeDistance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(radius));
        }

        _radius = radius;
        _swipeDistance = swipeDistance;
    }

    /// <summary>Generates logical input for this physical batch.</summary>
    /// <param name = "origin">The origin value.</param>
    /// <param name = "data">The data value.</param>
    /// <param name = "now">The now value.</param>
    /// <returns>The result of the operation.</returns>
    public IReadOnlyList<InputData> Generate(InputDeviceId origin, IReadOnlyList<InputData> data, TimeSpan now)
    {
        var result = new List<InputData>();
        foreach (InputData item in data)
        {
            if (item is FocusData { IsFocused: false } or DeviceDisconnectedData)
            {
                Reset(origin, now);
                _focused = false;
                result.Add(new FocusData(false));
            }
            else if (item is FocusData focus)
            {
                _focused = focus.IsFocused;
                result.Add(focus);
            }
            else if (_focused && item is TouchData touch)
            {
                if (touch.Phase == TouchPhase.Began)
                {
                    _contacts[touch.ContactId] = new Contact(touch.Position, touch.Position);
                    _distance = null;
                    _angle = null;
                }
                else if (_contacts.TryGetValue(touch.ContactId, out Contact? contact))
                {
                    if (touch.Phase is TouchPhase.Ended or TouchPhase.Canceled)
                    {
                        if (touch.Phase == TouchPhase.Ended && Vector2.Distance(contact.Start, touch.Position) >= _swipeDistance)
                        {
                            result.Add(new ControllerButtonData(ControllerButton.South, true));
                            result.Add(new ControllerButtonData(ControllerButton.South, false));
                        }

                        _contacts.Remove(touch.ContactId);
                        _distance = null;
                        _angle = null;
                    }
                    else
                    {
                        _contacts[touch.ContactId] = contact with
                        {
                            Current = touch.Position,
                        };
                    }
                }

                Vector2 stick = _contacts.Count == 1 ? (_contacts.Values.First().Current - _contacts.Values.First().Start) / _radius : Vector2.Zero;
                result.Add(new ControllerStickData(ControllerStick.Left, stick.LengthSquared() > 1 ? Vector2.Normalize(stick) : stick));
                Vector2 gesture = Vector2.Zero;
                if (_contacts.Count == 2)
                {
                    Contact[] pair = _contacts.Values.ToArray();
                    Vector2 delta = pair[1].Current - pair[0].Current;
                    float distance = delta.Length();
                    float angle = MathF.Atan2(delta.Y, delta.X);
                    _distance ??= distance;
                    _angle ??= angle;
                    float rotation = MathF.IEEERemainder(angle - _angle.Value, MathF.Tau);
                    gesture = new Vector2(Math.Clamp((distance - _distance.Value) / _radius, -1, 1), rotation / MathF.PI);
                }

                result.Add(new ControllerStickData(ControllerStick.Right, gesture));
            }
        }

        return result;
    }

    /// <summary>Discards transient recognition or correction state.</summary>
    /// <param name = "origin">The origin value.</param>
    /// <param name = "now">The now value.</param>
    public void Reset(InputDeviceId origin, TimeSpan now)
    {
        _contacts.Clear();
        _distance = null;
        _angle = null;
    }

    private sealed record Contact(Vector2 Start, Vector2 Current);
}
