using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.Application;
using Lumyte.UI.Application.Navigation;
using Lumyte.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using static Lumyte.UI.Application.Compose;
using static Lumyte.UI.Application.Navigation.UiPageWindows;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Pages.SampleSupport;

internal static class ExamplePages
{
    internal static SampleState Configure(UiApplicationBuilder builder, Action<UiApplicationWindowContext>? observe = null)
    {
        var state = new SampleState();
        builder.Services.AddSingleton(state);
        builder.Services.AddScoped<PageCounter>();
        UiPageKey<DetailArguments> details = builder.MapGroup("/details").MapPage<DetailsPage, DetailArguments>("/", static value => value);
        UiPageKey<UiNoArgs> home = builder.MapPage<PageCounter>("/", (context, counter) =>
        {
            _ = counter;
            state.Navigation = context.Activation.Navigation;
            return Button(width: state.Width, height: 48, testId: "next", onClick: () =>
            {
                state.Width.Value = 140;
                context.Activation.Navigation.RequestNavigate(details, new("Typed DI page"));
            })[Rectangle(width: state.Width, height: 48, fill: new Color2D(0.5f, 0.5f, 0.5f, 1))];
        });
        builder.UseWindows(_ => [Window("main", "DI pages", content: services => new ObservedWindow(Page(home)(services), state, observe))]);
        return state;
    }

    private sealed record DetailArguments(string Message);

    private sealed class DetailsPage(UiPageContext injected, PageCounter counter) : IUiPage
    {
        public async ValueTask PrepareAsync(UiPageContext context, CancellationToken cancellationToken)
        {
            if (!ReferenceEquals(injected, context) || context.Activation.Destination.GetArguments<DetailArguments>().Message != "Typed DI page")
            {
                throw new InvalidOperationException("The page scope did not receive the typed activation context.");
            }

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }

        public Widget Build(UiPageContext context)
        {
            if (!context.Activation.Dispatcher.CheckAccess())
            {
                throw new InvalidOperationException("Page construction must run on the UI owner.");
            }

            counter.State.Navigation = context.Activation.Navigation;
            return Button(width: 160, height: 48, testId: "quit", onClick: counter.State.Stop)[Rectangle(width: 160, height: 48, fill: new Color2D(0.25f, 0.5f, 0.75f, 1))];
        }
    }

    private sealed class PageCounter : IAsyncDisposable
    {
        public PageCounter(SampleState state)
        {
            State = state;
            state.Created++;
        }

        internal SampleState State { get; }

        public ValueTask DisposeAsync()
        {
            State.Disposed++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ObservedWindow(IUiApplicationContent inner, SampleState state, Action<UiApplicationWindowContext>? observe) : IUiApplicationContent, IUiApplicationGraphicsContent
    {
        public bool IsStopped => inner.IsStopped;

        public ValueTask InitializeAsync(UiApplicationWindowContext context, CancellationToken cancellationToken)
        {
            state.Context = context;
            observe?.Invoke(context);
            return inner.InitializeAsync(context, cancellationToken);
        }

        public void Advance() => inner.Advance();

        public void BeginStop() => inner.BeginStop();

        public ValueTask RetireAsync() => inner.RetireAsync();

        public ValueTask ReleaseAsync() => inner.ReleaseAsync();

        public ValueTask RebuildGraphicsAsync(UiApplicationWindowContext context) => (inner as IUiApplicationGraphicsContent)?.RebuildGraphicsAsync(context) ?? ValueTask.CompletedTask;
    }
}
