using System.Numerics;

using Lumyte.Input;
using Lumyte.Interaction;
using Lumyte.Platform;

using static Lumyte.Interaction.InteractionKit;
namespace Lumyte.DevTools.Host;

public sealed class DemoInputDomain : IDisposable
{
    public static readonly DevToolsDomain Domain = new("input");
    public static readonly DevToolsQuery<InputSnapshotRequest, InputSnapshot> GetState = new("getState");
    public static readonly DevToolsCommand<InjectKeyRequest, InputSnapshot> InjectKey = new("injectKey");
    public static readonly DevToolsCommand<InjectMouseButtonRequest, InputSnapshot> InjectMouseButton = new("injectMouseButton");
    public static readonly DevToolsCommand<InjectPointerRequest, InputSnapshot> InjectPointer = new("injectPointer");
    public static readonly DevToolsCommand<InjectWheelRequest, InputSnapshot> InjectWheel = new("injectWheel");
    public static readonly DevToolsCommand<ReleaseAllInputRequest, InputSnapshot> ReleaseAll = new("releaseAll");
    public static readonly DevToolsCommand<CaptureLeaseRequest, InputSnapshot> CaptureLease = new("captureLease");
    public static readonly DevToolsEvent<InputChanged> Changed = new("inputChanged");
    private readonly VirtualInputDevice devices = new();
    private readonly ActionRuntime runtime;
    private readonly List<WindowInputAttachment> windows = [];
    private readonly Lock inputSync = new();
    private readonly IDisposable[] registrations;
    private readonly DevToolsEventPublisher<InputChanged> publisher;
    private readonly Timer leaseTimer;
    private DateTimeOffset leaseDeadline;

    public DemoInputDomain(DevToolsHub hub)
    {
        var jump = new InputAction<bool>("game.jump");
        var interact = new InputAction<bool>("game.interact");
        var fire = new InputAction<bool>("game.fire");
        var look = new InputAction<Vector2>("game.look");
        var zoom = new InputAction<Vector2>("game.zoom");
        ActionMap gameplay = ActionMap("Gameplay")[
            new ActionBinding<bool>(jump, InputControls.Key(Key.Space)) { BindingId = "keyboard-space" },
            new ActionBinding<bool>(interact, InputControls.Key(Key.E)) { BindingId = "keyboard-e" },
            new ActionBinding<bool>(fire, InputControls.MouseButton(MouseButton.Left)) { BindingId = "mouse-primary" },
            new ActionBinding<Vector2>(look, InputControls.MouseDelta) { BindingId = "mouse-look" },
            new ActionBinding<Vector2>(zoom, InputControls.MouseWheel) { BindingId = "mouse-wheel" }];
        runtime = new ActionRuntime(new InteractionContext(), [gameplay], [devices], [devices]);
        publisher = hub.RegisterEvent(Domain, Changed);
        registrations = [hub.RegisterQuery(Domain, GetState, GetAsync), hub.RegisterCommand(Domain, InjectKey, InjectKeyAsync), hub.RegisterCommand(Domain, InjectMouseButton, InjectMouseButtonAsync), hub.RegisterCommand(Domain, InjectPointer, InjectPointerAsync), hub.RegisterCommand(Domain, InjectWheel, InjectWheelAsync), hub.RegisterCommand(Domain, ReleaseAll, ReleaseAllAsync), hub.RegisterCommand(Domain, CaptureLease, CaptureLeaseAsync)];
        leaseTimer = new Timer(_ => ExpireLease(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }
    public void Dispose() { leaseTimer.Dispose(); foreach (WindowInputAttachment window in windows.ToArray()) { DetachWindow(window); } foreach (IDisposable registration in registrations.Reverse()) { registration.Dispose(); } publisher.Dispose(); runtime.Dispose(); }
    public IDisposable AttachWindow(IWindow window, IWindowInput input, string deviceId) { var attachment = new WindowInputAttachment(window, input, deviceId, (device, control, value, transient) => NativeChanged(deviceId, device, control, value, transient)); windows.Add(attachment); runtime.AddKeyboard(attachment.Keyboard); runtime.AddMouse(attachment.Mouse); return new AttachmentLifetime(() => DetachWindow(attachment)); }
    private void DetachWindow(WindowInputAttachment attachment) { if (!windows.Remove(attachment)) { return; } runtime.RemoveKeyboard(attachment.Keyboard); runtime.RemoveMouse(attachment.Mouse); attachment.Dispose(); }
    private void NativeChanged(string id, string device, string control, object? value, bool transient) { InputSnapshot snapshot = Snapshot(); IReadOnlyList<InputRoute> routes = Routes(snapshot.Actions, device, control); publisher.PublishAsync(new(DateTimeOffset.UtcNow, "window", id, device, control, value, routes, snapshot)).AsTask().GetAwaiter().GetResult(); if (transient) { runtime.ResetTransientValues(); windows.FirstOrDefault(window => window.Id == id)?.NeutralizeAxes(); } }
    private ValueTask<InputSnapshot> GetAsync(InputSnapshotRequest _, CancellationToken token) { token.ThrowIfCancellationRequested(); return ValueTask.FromResult(Snapshot()); }
    private ValueTask<InputSnapshot> InjectKeyAsync(InjectKeyRequest request, CancellationToken token) => ChangeAsync("browser", "keyboard", request.Key, request.IsPressed, () => devices.SetKey(ParseKey(request.Key), request.IsPressed, request.IsRepeat), token);
    private ValueTask<InputSnapshot> InjectMouseButtonAsync(InjectMouseButtonRequest request, CancellationToken token) => ChangeAsync("browser", "mouse", request.Button, request.IsPressed, () => devices.SetButton(ParseButton(request.Button), request.IsPressed), token);
    private ValueTask<InputSnapshot> InjectPointerAsync(InjectPointerRequest request, CancellationToken token) => ChangeAsync("browser", "mouse", "Delta", new { x = request.DeltaX, y = request.DeltaY }, () => devices.Move(new(request.X, request.Y), new(request.DeltaX, request.DeltaY)), token, true);
    private ValueTask<InputSnapshot> InjectWheelAsync(InjectWheelRequest request, CancellationToken token) => ChangeAsync("browser", "mouse", "Wheel", new { x = request.DeltaX, y = request.DeltaY }, () => devices.Scroll(new(request.DeltaX, request.DeltaY)), token, true);
    private ValueTask<InputSnapshot> CaptureLeaseAsync(CaptureLeaseRequest request, CancellationToken token) { token.ThrowIfCancellationRequested(); leaseDeadline = request.Active ? DateTimeOffset.UtcNow.AddSeconds(3) : default; if (!request.Active) { lock (inputSync) { devices.ReleaseAll(); runtime.ResetTransientValues(); } } return ValueTask.FromResult(Snapshot()); }
    private void ExpireLease() { if (leaseDeadline == default || DateTimeOffset.UtcNow <= leaseDeadline) { return; } leaseDeadline = default; lock (inputSync) { devices.ReleaseAll(); runtime.ResetTransientValues(); } InputSnapshot snapshot = Snapshot(); publisher.PublishAsync(new(DateTimeOffset.UtcNow, "lease-timeout", "browser", "all", "releaseAll", false, [], snapshot)).AsTask().GetAwaiter().GetResult(); }
    private async ValueTask<InputSnapshot> ReleaseAllAsync(ReleaseAllInputRequest request, CancellationToken token) { lock (inputSync) { devices.ReleaseAll(); runtime.ResetTransientValues(); } InputSnapshot snapshot = Snapshot(); await publisher.PublishAsync(new(DateTimeOffset.UtcNow, request.Source ?? "browser", "browser", "all", "releaseAll", false, [], snapshot), token); return snapshot; }
    private async ValueTask<InputSnapshot> ChangeAsync(string source, string device, string control, object? value, Action change, CancellationToken token, bool resetTransient = false) { lock (inputSync) { change(); } InputSnapshot snapshot = Snapshot(); IReadOnlyList<InputRoute> routes = Routes(snapshot.Actions, device, control); await publisher.PublishAsync(new(DateTimeOffset.UtcNow, source, "browser", device, control, value, routes, snapshot), token); if (resetTransient) { runtime.ResetTransientValues(); devices.NeutralizeAxes(); } return snapshot; }
    private InputSnapshot Snapshot() { lock (inputSync) { RawInputSourceSnapshot browser = devices.Snapshot(); RawInputSourceSnapshot[] sources = [browser, .. windows.Select(window => window.Snapshot())]; RawInputSnapshot raw = new([.. sources.SelectMany(source => source.PressedKeys).Distinct()], [.. sources.SelectMany(source => source.PressedMouseButtons).Distinct()], browser.PointerPosition, browser.PointerDelta, browser.WheelDelta, sources); return new(raw, runtime.GetSnapshot()); } }
    private static IReadOnlyList<InputRoute> Routes(ActionRuntimeSnapshot snapshot, string device, string control) { string expected = $"{device}/{control}"; return [.. snapshot.Maps.SelectMany(map => map.Bindings.Select(binding => (map, binding))).Where(item => StringComparer.OrdinalIgnoreCase.Equals(item.binding.Control, expected)).Select(item => { ActionStateSnapshot? state = snapshot.Actions.FirstOrDefault(action => action.Id == item.binding.ActionId); return new InputRoute(item.map.Name, item.binding.BindingId, item.binding.ActionId, state?.Value, state?.Phase.ToString() ?? "Waiting"); })]; }
    private static Key ParseKey(string value) => Enum.TryParse(value, true, out Key key) && key != Key.Unknown ? key : throw new ArgumentException($"Unknown keyboard key '{value}'.", nameof(value));
    private static MouseButton ParseButton(string value) => Enum.TryParse(value, true, out MouseButton button) ? button : throw new ArgumentException($"Unknown mouse button '{value}'.", nameof(value));

    private sealed class VirtualInputDevice : IKeyboard, IMouse
    {
        private readonly Lock sync = new(); private readonly HashSet<Key> keys = []; private readonly HashSet<MouseButton> buttons = []; private Vector2 position, delta, wheel;
        public event EventHandler<KeyChangedEventArgs>? KeyChanged; public event EventHandler<MouseMovedEventArgs>? Moved; public event EventHandler<RawMouseMovedEventArgs>? RawMoved; public event EventHandler<MouseButtonChangedEventArgs>? ButtonChanged; public event EventHandler<MouseWheelChangedEventArgs>? WheelChanged;
        public Vector2 Position { get { lock (sync) { return position; } } }
        public bool IsCursorVisible { get; set; } = true; public CursorMode CursorMode { get; set; }
        public bool IsKeyPressed(Key key) { lock (sync) { return keys.Contains(key); } }
        public bool IsButtonPressed(MouseButton button) { lock (sync) { return buttons.Contains(button); } }
        public void SetKey(Key key, bool pressed, bool repeat) { bool changed; lock (sync) { changed = pressed ? keys.Add(key) : keys.Remove(key); } if (changed || repeat) { KeyChanged?.Invoke(this, new(key, pressed, repeat)); } }
        public void SetButton(MouseButton button, bool pressed) { bool changed; Vector2 current; lock (sync) { changed = pressed ? buttons.Add(button) : buttons.Remove(button); current = position; } if (changed) { ButtonChanged?.Invoke(this, new(button, pressed, current)); } }
        public void Move(Vector2 next, Vector2 movement) { lock (sync) { position = next; delta = movement; } Moved?.Invoke(this, new(next, movement)); RawMoved?.Invoke(this, new(movement)); }
        public void Scroll(Vector2 movement) { lock (sync) { wheel = movement; } WheelChanged?.Invoke(this, new(movement)); }
        public void NeutralizeAxes() { lock (sync) { delta = Vector2.Zero; wheel = Vector2.Zero; } }
        public void ReleaseAll() { Key[] heldKeys; MouseButton[] heldButtons; lock (sync) { heldKeys = [.. keys]; heldButtons = [.. buttons]; keys.Clear(); buttons.Clear(); delta = Vector2.Zero; wheel = Vector2.Zero; } foreach (Key key in heldKeys) { KeyChanged?.Invoke(this, new(key, false, false)); } foreach (MouseButton button in heldButtons) { ButtonChanged?.Invoke(this, new(button, false, Position)); } }
        public RawInputSourceSnapshot Snapshot() { lock (sync) { return new("browser", "browser", [.. keys.Order().Select(key => key.ToString())], [.. buttons.Order().Select(button => button.ToString())], position, delta, wheel); } }
    }
}
public sealed record InputSnapshotRequest;
public sealed record InjectKeyRequest(string Key, bool IsPressed, bool IsRepeat = false);
public sealed record InjectMouseButtonRequest(string Button, bool IsPressed);
public sealed record InjectPointerRequest(float X, float Y, float DeltaX, float DeltaY);
public sealed record InjectWheelRequest(float DeltaX, float DeltaY);
public sealed record ReleaseAllInputRequest(string? Source = null);
public sealed record CaptureLeaseRequest(bool Active);
public sealed record RawInputSourceSnapshot(string Source, string DeviceId, IReadOnlyList<string> PressedKeys, IReadOnlyList<string> PressedMouseButtons, Vector2 PointerPosition, Vector2 PointerDelta, Vector2 WheelDelta);
public sealed record RawInputSnapshot(IReadOnlyList<string> PressedKeys, IReadOnlyList<string> PressedMouseButtons, Vector2 PointerPosition, Vector2 PointerDelta, Vector2 WheelDelta, IReadOnlyList<RawInputSourceSnapshot> Sources);
public sealed record InputSnapshot(RawInputSnapshot Raw, ActionRuntimeSnapshot Actions);
public sealed record InputRoute(string Map, string? Binding, string Action, object? Value, string Phase);
public sealed record InputChanged(DateTimeOffset Timestamp, string Source, string DeviceId, string Device, string Control, object? Value, IReadOnlyList<InputRoute> Routes, InputSnapshot Snapshot);
file sealed class AttachmentLifetime(Action dispose) : IDisposable { private Action? action = dispose; public void Dispose() => Interlocked.Exchange(ref action, null)?.Invoke(); }
