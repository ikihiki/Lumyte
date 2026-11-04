using Lumyte.Graphics.Drawing2D;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Navigation.SampleSupport;

internal sealed class ExamplePages(Action requestStop) : IUiPageActivationProvider
{
    internal static UiPageKey<UiNoArgs> Home { get; } = new("/");

    internal static UiPageKey<DetailArguments> Details { get; } = new("/details");

    public UiPageDestination Capture<TArgs>(UiPageKey<TArgs> key, TArgs arguments)
    {
        ArgumentNullException.ThrowIfNull(key);
        if ((key.Value == Home.Value && typeof(TArgs) == typeof(UiNoArgs)) || (key.Value == Details.Value && typeof(TArgs) == typeof(DetailArguments)))
        {
            return UiPageDestination.Capture(key, arguments, static value => value);
        }

        throw new ArgumentException("Unknown route or argument type.", nameof(key));
    }

    public IUiPageActivation Create(UiPageActivationContext context) => new Page(context, requestStop);

    internal sealed record DetailArguments(string Message);

    private sealed class Page(UiPageActivationContext context, Action requestStop) : IUiPageActivation
    {
        private Widget? _root;

        public bool IsPrepared => _root is not null;

        public Widget Root => _root ?? throw new InvalidOperationException("The page has not prepared.");

        public IUiPageLeaveGuard? LeaveGuard => null;

        public async ValueTask PrepareAsync(CancellationToken cancellationToken) => _root = await context.Dispatcher.InvokeAsync(Build, cancellationToken);

        public ValueTask RetireAsync(CancellationToken cancellationToken) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private Widget Build()
        {
            if (context.Destination.Route == Home.Value)
            {
                return Button(width: 160, height: 48, testId: "next", onClick: () => context.Navigation.RequestNavigate(Details, new("Prepared details")))[Rectangle(width: 160, height: 48, fill: new Color2D(0.5f, 0.5f, 0.5f, 1))];
            }

            _ = context.Destination.GetArguments<DetailArguments>();
            return Button(width: 160, height: 48, testId: "quit", onClick: requestStop)[Rectangle(width: 160, height: 48, fill: new Color2D(0.25f, 0.5f, 0.75f, 1))];
        }
    }
}
