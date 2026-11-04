using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application.Headless;

internal sealed class HeadlessDriver(UiHeadlessApplicationOptions options) : UiApplicationDriver
{
    public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

    internal override ValueTask InitializeAsync(UiApplication application, IServiceProvider services, bool textInput, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.CompletedTask;
    }

    internal override ValueTask<UiDriverWindow> CreateWindowAsync(string key, string title, Vector2 size, UiDriverWindow? owner, bool primary, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<UiDriverWindow>(new Window(key, primary ? options.ClientSize : size, options.Scale));
    }

    internal override void Pump()
    {
    }

    internal override Task<bool> DrawAsync(UiDriverWindow window, DrawList2D commands) => Task.FromResult(true);

    internal override ValueTask CloseWindowAsync(UiDriverWindow window) => window.DisposeAsync();

    private sealed class Window(string key, Vector2 size, Vector2 scale) : UiDriverWindow(key)
    {
        private readonly InputWindowId _origin = new(Guid.NewGuid());
        private readonly Guid _scope = Guid.NewGuid();
        private bool _visible;
        private bool _closed;
        private ulong _revision = 1;

        public override ValueTask DisposeAsync()
        {
            _closed = true;
            _revision++;
            return ValueTask.CompletedTask;
        }

        internal override UiWindowSourceSnapshot CaptureState() => new(_revision, _origin, size, scale, _visible, _visible, _closed, _scope);

        internal override void Show()
        {
            _visible = true;
            _revision++;
        }

        internal override void SetTitle(string title)
        {
        }
    }
}
