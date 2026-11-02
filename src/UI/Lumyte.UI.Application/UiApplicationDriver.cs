using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Platform.Abstractions;
using Lumyte.Platform.TextInput.Abstractions;
using Lumyte.UI.Windowing;

namespace Lumyte.UI.Application;

internal abstract class UiApplicationDriver : IAsyncDisposable
{
    internal virtual bool CurrentThread => false;

    internal virtual bool SupportsScreenshots => false;

    internal virtual bool NativeWindows => false;

    internal virtual bool IsPhysicalInputIdle => true;

    internal virtual IWindowInputSource? InputSource => null;

    internal virtual ITextInputService? TextInput => null;

    internal virtual IClipboard? Clipboard => null;

    internal virtual bool IsQuitRequested => false;

    internal Action<UiDriverWindow>? CloseRequested { get; set; }

    internal Func<Lumyte.Input.InputRecord, bool>? DispatchInput { get; set; }

    internal Action? SynchronizeFocus { get; set; }

    public abstract ValueTask DisposeAsync();

    internal virtual Lumyte.UI.Windowing.TextInput.IUiTextInputTargetResolver CreateTextTargets(UiApplication application) => new Lumyte.UI.Windowing.TextInput.UiPlatformTextInputTargetResolver(application.Windows, InputSource!, application.ResolveNative);

    internal virtual ValueTask<byte[]> CaptureAsync(UiSurface surface, CancellationToken token) => throw new NotSupportedException("This driver does not provide GPU readback.");

    internal abstract ValueTask InitializeAsync(UiApplication application, IServiceProvider services, bool textInput, CancellationToken cancellationToken);

    internal abstract ValueTask<UiDriverWindow> CreateWindowAsync(string key, string title, Vector2 size, UiDriverWindow? owner, bool primary, CancellationToken cancellationToken);

    internal abstract void Pump();

    internal abstract Task<bool> DrawAsync(UiDriverWindow window, DrawList2D commands);

    internal abstract ValueTask CloseWindowAsync(UiDriverWindow window);

    internal virtual IUiWindowManager CreateWindowManager(TimeProvider clock) => new HostedUiWindowManager(clock);

    internal virtual ValueTask RetireResourcesAsync() => ValueTask.CompletedTask;

    internal virtual ValueTask DrainWindowAsync(UiDriverWindow window) => ValueTask.CompletedTask;

    internal virtual Lumyte.Windowing.Abstractions.IWindow? ResolveNative(Guid source) => null;

    internal virtual void DispatchPendingInput()
    {
    }

    internal virtual void CompleteFrames()
    {
    }

    internal virtual bool AdvanceFrames() => false;

    internal virtual void WaitForWork(UiApplicationDispatcher dispatcher, TimeSpan timeout) => dispatcher.WaitForWork(timeout);

    internal virtual void EndOwnerWait()
    {
    }

    internal virtual ValueTask NextTurnAsync(TimeSpan interval) => new(Task.Delay(interval));
}
