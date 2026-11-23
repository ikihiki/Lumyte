using System.Numerics;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Application.WebView.Cef;
using Lumyte.UI.Windowing;
using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Browser detach remains local after a real window's presentation snapshot disappears.</summary>
public sealed class DestroyedWindowDetachTests
{
    /// <summary>Unmount uses the last validated scale, while visible updates still query and validate the window.</summary>
    /// <returns>The owner-loop verification.</returns>
    [Fact]
    public async Task SurfaceUnmountDoesNotQueryDisappearedWindowAsync()
    {
        UiApplicationWindowContext? window = null;
        UiWebViewSession? browser = null;
        SnapshotManager? manager = null;
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        typeof(UiApplicationBuilder).GetProperty("IntegrationDriverKind", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(builder, "desktop");
        builder.UseWindowManager(context =>
        {
            IUiWindowManager proxy = DispatchProxy.Create<IUiWindowManager, SnapshotManager>();
            manager = (SnapshotManager)proxy;
            manager.Inner = context.DefaultManager;
            return proxy;
        });
        builder.UseCefWebViews(new()).UseRoot(context =>
        {
            window = context;
            browser = context.GetWebViewSession("page", new("https://example.invalid/help"));
            return Compose.WebView(session: browser, width: 100, height: 80);
        });
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (app.State == UiApplicationState.Starting)
        {
            if (app.Completion.IsCompleted)
            {
                await app.Completion;
            }

            await Task.Delay(1, timeout.Token);
        }

        await app.Dispatcher.InvokeAsync(() =>
        {
            Assert.Equal(UiApplicationState.Running, app.State);
            manager!.Target = window!.Window;
            manager.Scale = 2;
            IWebViewHost host = browser!.Host!;
            host.Viewport(new(100, 80), true);
            Type entry = host.GetType();
            long generation = (long)entry.GetField("_generation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host)!;
            manager.Scale = float.NaN;
            Assert.Equal("cef-invalid-window-scale", Assert.Throws<InvalidOperationException>(() => host.Viewport(new(100, 80), true)).Message);
            manager.Scale = 2;
            manager.Removed = true;
            Assert.Equal("window disappeared before release", Assert.Throws<InvalidOperationException>(() => host.Viewport(new(100, 80), true)).Message);
            manager.MissingQueries = 0;
            Exception? failure;
            try
            {
                // Real WebViewElement.OnUnmounted -> UiWebViewSession.Detach -> Entry.Detach.
                UiSurface surface = window.View.CurrentSurface!;
                surface.Rebuild(Lumyte.UI.Compose.Button(width: 40, height: 20));
                failure = Record.Exception(surface.UpdateLayout);
            }
            finally
            {
                manager.Removed = false;
            }

            Assert.Null(failure);
            Assert.Equal(0, manager.MissingQueries);
            Assert.Equal(2F, entry.GetField("_scale", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host));
            Assert.Equal(false, entry.GetField("_visible", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host));
            Assert.Equal(generation + 1, entry.GetField("_generation", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(host));
        });
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await running.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(UiApplicationState.Stopped, app.State);
        Assert.Equal(UiWebViewState.Closed, browser!.State);
    }

    /// <summary>Forwards the real manager except for one explicitly removed window snapshot.</summary>
    public class SnapshotManager : DispatchProxy
    {
        internal IUiWindowManager Inner { get; set; } = null!;

        internal UiWindowId Target { get; set; }

        internal float Scale { get; set; } = 1;

        internal bool Removed { get; set; }

        internal int MissingQueries { get; set; }

        /// <inheritdoc/>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name == nameof(IUiWindowManager.TryGetSnapshot) && args is not null && args[0] is UiWindowId id && id == Target)
            {
                if (Removed)
                {
                    MissingQueries++;
                    throw new InvalidOperationException("window disappeared before release");
                }

                bool found = Inner.TryGetSnapshot(id, out UiWindowSnapshot? snapshot);
                args[1] = snapshot is null ? null : snapshot with { Scale = new Vector2(Scale) };
                return found;
            }

            try
            {
                return targetMethod.Invoke(Inner, args);
            }
            catch (TargetInvocationException error) when (error.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(error.InnerException).Throw();
                throw;
            }
        }
    }
}
