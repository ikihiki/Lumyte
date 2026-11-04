using Lumyte.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Navigation;

internal sealed class UiPageRegistry(IReadOnlyDictionary<string, UiPageRegistration> pages, Type[] services)
{
    internal IUiPageActivationProvider CreateProvider(UiApplicationWindowContext window) => new Provider(pages, services, window);

    private sealed class Provider(IReadOnlyDictionary<string, UiPageRegistration> registrations, Type[] services, UiApplicationWindowContext window) : IUiPageActivationProvider
    {
        public UiPageDestination Capture<TArgs>(UiPageKey<TArgs> key, TArgs arguments)
        {
            ArgumentNullException.ThrowIfNull(key);
            return Find(key.Value, typeof(TArgs)).Capture(key, arguments);
        }

        public IUiPageActivation Create(UiPageActivationContext context) => new Activation(Find(context.Destination.Route, context.Destination.ArgumentsType), window, context, services);

        private UiPageRegistration Find(string route, Type type) => registrations.TryGetValue(route, out UiPageRegistration? page) && page.ArgumentsType == type ? page : throw new ArgumentException("The page route and argument type are not registered.");
    }

    private sealed class Activation(UiPageRegistration registration, UiApplicationWindowContext window, UiPageActivationContext activation, Type[] serviceTypes) : IUiPageActivation
    {
        private AsyncServiceScope? _scope;
        private IUiPage? _page;
        private UiPageContext? _context;
        private Widget? _root;
        private bool _started;
        private bool _retired;
        private Task? _dispose;

        public bool IsPrepared => _root is not null;

        public Widget Root => _root ?? throw new InvalidOperationException("The page has not been prepared.");

        public IUiPageLeaveGuard? LeaveGuard => _page?.LeaveGuard;

        public async ValueTask PrepareAsync(CancellationToken cancellationToken)
        {
            if (_started)
            {
                throw new InvalidOperationException("An activation can only prepare once.");
            }

            _started = true;
            cancellationToken.ThrowIfCancellationRequested();
            _scope = window.Services.CreateAsyncScope();
            IServiceProvider services = _scope.Value.ServiceProvider;
            var context = new UiPageContext(activation, window.TextLayoutProvider);
            _context = context;
            context.ServiceTypes = serviceTypes;
            services.GetRequiredService<UiPageContextBinding>().Bind(context);
            _page = registration.Create(services);
            context.SetOwnershipAdmission(true);
            try
            {
                await _page.PrepareAsync(context, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                context.SetOwnershipAdmission(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            _root = await activation.Dispatcher.InvokeAsync(() => UiSurface.CaptureBuild(() => context.Build(() => _page.Build(context))), cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask RetireAsync(CancellationToken cancellationToken)
        {
            _context?.Retire();
            if (!_retired && _page is not null)
            {
                await _page.RetireAsync(cancellationToken).ConfigureAwait(false);
                _retired = true;
            }
        }

        public ValueTask DisposeAsync() => new(_dispose ??= DisposeScopeAsync());

        private async Task DisposeScopeAsync()
        {
            List<Exception> errors = [];
            try
            {
                _context?.DisposeOwned();
            }
            catch (Exception error)
            {
                errors.Add(error);
            }

            if (_scope is { } scope)
            {
                try
                {
                    await scope.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            if (errors.Count != 0)
            {
                throw new AggregateException(errors);
            }
        }
    }
}
