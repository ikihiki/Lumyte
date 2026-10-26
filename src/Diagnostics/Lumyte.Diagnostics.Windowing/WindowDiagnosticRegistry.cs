using Lumyte.Windowing;

namespace Lumyte.Diagnostics.Windowing;

/// <summary>Registers WindowSystem instances for scoped diagnostic access.</summary>
public sealed class WindowDiagnosticRegistry
{
    internal const int MaximumSystems = 64;
    private readonly Dictionary<string, Target> _targets = new(StringComparer.Ordinal);

    /// <summary>Registers one WindowSystem under a stable lowercase ID.</summary>
    /// <param name="id">The diagnostic system ID.</param>
    /// <param name="system">The WindowSystem to expose.</param>
    /// <returns>A registration which removes only this system when disposed.</returns>
    public WindowDiagnosticRegistration Register(string id, WindowSystem system)
    {
        ArgumentNullException.ThrowIfNull(system);
        ValidateName(id, nameof(id));
        if (_targets.Count >= MaximumSystems)
        {
            throw new InvalidOperationException("The window diagnostic system limit was reached.");
        }

        var target = new Target(id, system);
        if (!_targets.TryAdd(id, target))
        {
            target.Dispose();
            throw new ArgumentException("The window diagnostic system ID is already registered.", nameof(id));
        }

        return new(this, id, target);
    }

    internal string List() => string.Join('\n', _targets.Keys.Order(StringComparer.Ordinal));

    internal bool TryGet(string id, out Target? target) => _targets.TryGetValue(id, out target);

    internal void Remove(string id, object target)
    {
        if (_targets.TryGetValue(id, out Target? current) && ReferenceEquals(current, target))
        {
            _targets.Remove(id);
            current.Dispose();
        }
    }

    private static void ValidateName(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        if (value.Length > 128 || value.Any(character => character is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.')))
        {
            throw new ArgumentException("Diagnostic names must use 1-128 lowercase ASCII identifier characters.", parameter);
        }
    }

    internal sealed class Target : IDisposable
    {
        private readonly Dictionary<WindowNode, string> _ids = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, WindowNode> _windows = new(StringComparer.Ordinal);
        private readonly WindowSystem _system;
        private ulong _next;

        public Target(string id, WindowSystem system)
        {
            Id = id;
            _system = system;
            _system.Removed += Removed;
        }

        public string Id { get; }

        public string List()
        {
            Synchronize();
            return string.Join('\n', _windows.Keys.Order(StringComparer.Ordinal));
        }

        public bool TryGet(string id, out WindowNode? node)
        {
            Synchronize();
            return _windows.TryGetValue(id, out node);
        }

        public WindowDiagnosticSnapshot Snapshot(WindowNode node)
        {
            string id = GetId(node);
            return new(
                Id,
                id,
                node.Parent is { } parent ? GetId(parent) : string.Empty,
                GetId(node.PresentationRoot),
                node.Kind.ToString(),
                node.Role.ToString(),
                node.Ownership.ToString(),
                Format(node.NativeWindowId),
                node.Bounds.X,
                node.Bounds.Y,
                node.Bounds.Width,
                node.Bounds.Height,
                node.ClientSize.X,
                node.ClientSize.Y,
                node.PixelSize.Width,
                node.PixelSize.Height,
                node.Scale.X,
                node.Scale.Y,
                node.IsVisible,
                node.IsEffectivelyVisible,
                node.Lifetime.ToString());
        }

        public void Dispose()
        {
            _system.Removed -= Removed;
            _ids.Clear();
            _windows.Clear();
        }

        private static string Format(Lumyte.Windowing.Abstractions.WindowId? id)
            => id is { } value ? $"{value.PlatformId:D}:{value.Value}" : string.Empty;

        private string GetId(WindowNode node)
        {
            if (_ids.TryGetValue(node, out string? id))
            {
                return id;
            }

            id = $"window-{checked(++_next)}";
            _ids.Add(node, id);
            _windows.Add(id, node);
            return id;
        }

        private void Synchronize()
        {
            foreach (WindowNode node in _system.Windows)
            {
                _ = GetId(node);
            }
        }

        private void Removed(WindowNode node)
        {
            if (_ids.Remove(node, out string? id))
            {
                _windows.Remove(id);
            }
        }
    }
}
