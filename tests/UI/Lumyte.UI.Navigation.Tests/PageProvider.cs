using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Navigation.Tests;

internal sealed class PageProvider : IUiPageActivationProvider
{
    private readonly Dictionary<string, Registration> _routes = [];

    internal List<Activation> Activations { get; } = [];

    public UiPageDestination Capture<TArgs>(UiPageKey<TArgs> key, TArgs arguments)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_routes.TryGetValue(key.Value, out Registration? registration) || registration.Type != typeof(TArgs))
        {
            throw new ArgumentException("Unknown route or exact argument type mismatch.", nameof(key));
        }

        return UiPageDestination.Capture(key, arguments, (Func<TArgs, TArgs>)registration.Copier);
    }

    public IUiPageActivation Create(UiPageActivationContext context)
    {
        var activation = new Activation(context, _routes[context.Destination.Route].Build);
        Activations.Add(activation);
        return activation;
    }

    internal UiPageKey<TArgs> Register<TArgs>(string route, Func<TArgs, TArgs> copy, Func<Activation, CancellationToken, ValueTask<Widget>> build)
    {
        var key = new UiPageKey<TArgs>(route);
        _routes.Add(route, new(typeof(TArgs), copy, build));
        return key;
    }

    internal UiPageKey<UiNoArgs> Register(string route, Func<Activation, CancellationToken, ValueTask<Widget>> build) => Register<UiNoArgs>(route, static args => args, build);

    private sealed record Registration(Type Type, Delegate Copier, Func<Activation, CancellationToken, ValueTask<Widget>> Build);

    internal sealed class Activation(UiPageActivationContext context, Func<Activation, CancellationToken, ValueTask<Widget>> build) : IUiPageActivation
    {
        private Widget? _root;

        public bool IsPrepared => _root is not null;

        public Widget Root => _root ?? throw new InvalidOperationException("Not prepared.");

        public IUiPageLeaveGuard? LeaveGuard { get; set; }

        internal UiPageActivationContext Context { get; } = context;

        internal int Retires { get; private set; }

        internal int Disposals { get; private set; }

        internal Func<ValueTask>? Retire { get; set; }

        internal Func<ValueTask>? Dispose { get; set; }

        public async ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            Widget root = await build(this, cancellationToken).ConfigureAwait(false);
            _root = await Context.Dispatcher.InvokeAsync(() => root, cancellationToken);
        }

        public async ValueTask RetireAsync(CancellationToken cancellationToken)
        {
            Retires++;
            if (Retire is { } cleanup)
            {
                await cleanup();
            }
        }

        public async ValueTask DisposeAsync()
        {
            Disposals++;
            if (Dispose is { } dispose)
            {
                await dispose();
            }
        }
    }
}
