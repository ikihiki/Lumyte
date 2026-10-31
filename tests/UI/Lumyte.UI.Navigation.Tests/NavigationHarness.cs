using Lumyte.UI.Application;
using Lumyte.UI.Application.Diagnostics;
using Lumyte.UI.Application.Headless;
using Lumyte.UI.Application.Navigation;
using Lumyte.UI.Application.Navigation.Diagnostics;
using static Lumyte.UI.Application.Compose;

namespace Lumyte.UI.Navigation.Tests;

internal sealed class NavigationHarness : IAsyncDisposable
{
    private readonly Task _run;

    internal NavigationHarness(PageProvider provider, Func<IUiNavigator, UiNavigationRequest> initial, UiNavigationOptions? options = null, bool diagnostics = false, Action<UiApplicationBuilder>? configure = null, Action<UiNavigationResult>? onCompleted = null, bool navigationDiagnostics = true)
    {
        Content = new(provider, initial, options, onCompleted);
        var observed = new ObservedContent(this, Content);
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        if (diagnostics)
        {
            builder.UseDiagnostics(new() { Target = new() { AllowInput = true, AllowTextInspection = true }, Runtime = new() { MaximumTargets = 1 } });
            if (navigationDiagnostics)
            {
                builder.UseNavigationDiagnostics();
            }
        }

        configure?.Invoke(builder);
        builder.UseWindows(_ => [Window("main", "Navigation", content: _ => observed)]);
        App = builder.Build();
        _run = App.RunAsync();
    }

    internal UiApplication App { get; }

    internal UiNavigationApplicationContent Content { get; }

    internal UiApplicationWindowContext Context { get; private set; } = null!;

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await _run.WaitAsync(TimeSpan.FromSeconds(10));
        await App.DisposeAsync();
    }

    internal async Task ReadyAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (App.State == UiApplicationState.Starting)
        {
            if (App.Completion.IsCompleted)
            {
                await App.Completion;
            }

            await Task.Delay(1, timeout.Token);
        }

        if (App.State != UiApplicationState.Running)
        {
            await App.Completion;
            throw new InvalidOperationException("The application did not start.");
        }

        await IdleAsync();
    }

    internal async Task WaitAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await App.Dispatcher.InvokeAsync(predicate, timeout.Token))
        {
            if (App.Completion.IsCompleted)
            {
                await App.Completion;
            }

            await Task.Delay(1, timeout.Token);
        }
    }

    internal Task IdleAsync() => WaitAsync(() => Content.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle);

    internal Task<UiNavigationSnapshot> SnapshotAsync() => App.Dispatcher.InvokeAsync(Content.Navigation.CaptureSnapshot).AsTask();

    private sealed class ObservedContent(NavigationHarness harness, UiNavigationApplicationContent inner) : IUiApplicationContent, IUiApplicationGraphicsContent
    {
        public bool IsStopped => inner.IsStopped;

        public ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken)
        {
            harness.Context = context;
            return inner.InitializeAsync(context, cancellationToken);
        }

        public void Advance() => inner.Advance();

        public void BeginStop() => inner.BeginStop();

        public ValueTask RetireAsync() => inner.RetireAsync();

        public ValueTask ReleaseAsync() => inner.ReleaseAsync();

        public ValueTask RebuildGraphicsAsync(UiApplicationWindowContext context) => inner.RebuildGraphicsAsync(context);
    }
}
