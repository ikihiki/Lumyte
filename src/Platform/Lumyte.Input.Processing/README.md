# Lumyte.Input.Processing

Device correction and logical controller generation for `Lumyte.Input`.

Wrap an injected backend with `CorrectingInputSource`, then optionally wrap it
with `VirtualizingInputSource`. Register only the outer source as `IInputSource`.
Each physical batch is acquired once, corrected before recording, and routed to
physical and virtual devices in the same update. Sources borrow their inner
sources; InputSystem owns successfully registered devices.

Built-in processors provide center calibration, radial dead zones, pressure
curves and time-based stick smoothing. `TouchControllerGenerator` maps a touch
joystick, completed swipes, pinch and rotation to controller controls.

Use `InputTimeSource` to bind callbacks to InputSystem time after construction.
Replace processors or generators on the owning thread; changes take effect at
the next input boundary and cancel in-flight operations.

See [the ADR](../../../docs/adr/input/INPUT-0002-processing-and-recognition.md)
and [the runnable sample](../../../samples/Lumyte.Input.Advanced.Sample/README.md).
