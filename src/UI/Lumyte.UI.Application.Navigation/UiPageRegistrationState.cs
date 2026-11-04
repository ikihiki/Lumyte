using System.Runtime.CompilerServices;
using Lumyte.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Application.Navigation;

internal sealed class UiPageRegistrationState
{
    private static readonly ConditionalWeakTable<UiApplicationBuilder, UiPageRegistrationState> _states = new();
    private readonly UiApplicationBuilder _builder;
    private readonly Dictionary<string, UiPageRegistration> _pages = new(StringComparer.Ordinal);
    private readonly ServiceDescriptor _binding;
    private readonly ServiceDescriptor _context;
    private readonly ServiceDescriptor _registry;
    private Dictionary<string, UiPageRegistration>? _snapshot;
    private UiPageKey<UiNoArgs>? _initial;
    private Type[] _serviceTypes = [];

    private UiPageRegistrationState(UiApplicationBuilder builder)
    {
        _builder = builder;
        _binding = ServiceDescriptor.Scoped<UiPageContextBinding, UiPageContextBinding>();
        _context = ServiceDescriptor.Scoped<UiPageContext>(services => services.GetRequiredService<UiPageContextBinding>().Context);
        _registry = ServiceDescriptor.Singleton<UiPageRegistry>(_ => new(_snapshot ?? throw new InvalidOperationException("Build has not frozen page registrations."), _serviceTypes));
        builder.Services.Add(_binding);
        builder.Services.Add(_context);
        builder.Services.Add(_registry);
        builder.BuildValidators.Add(Validate);
    }

    internal static UiPageRegistrationState Get(UiApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.CheckMutable();
        return _states.GetValue(builder, static value => new(value));
    }

    internal static void ValidatePageService(IServiceCollection services, Type pageType)
    {
        ServiceDescriptor[] found = services.Where(descriptor => descriptor.ServiceType == pageType && !descriptor.IsKeyedService).ToArray();
        if (found.Length > 1 || found.Any(descriptor => descriptor.Lifetime != ServiceLifetime.Scoped || descriptor.ImplementationInstance is not null))
        {
            throw new ArgumentException("A page must have exactly one unkeyed scoped registration.", nameof(pageType));
        }
    }

    internal void CheckRoute(string route)
    {
        _builder.CheckMutable();
        if (_pages.ContainsKey(route))
        {
            throw new ArgumentException("The page route is already registered.", nameof(route));
        }
    }

    internal void Add(UiPageRegistration page) => _pages.Add(page.Route, page);

    internal void UseInitial(UiPageKey<UiNoArgs> key, Func<IServiceProvider, IUiApplicationContent> content)
    {
        _builder.CheckMutable();
        if (_initial is not null)
        {
            throw new InvalidOperationException("Navigation was already configured.");
        }

        _builder.UseContent(content);
        _initial = key;
    }

    private static void RequireOwned(IServiceCollection services, ServiceDescriptor expected)
    {
        ServiceDescriptor[] found = services.Where(descriptor => descriptor.ServiceType == expected.ServiceType).ToArray();
        if (found.Length != 1 || !ReferenceEquals(found[0], expected))
        {
            throw new ArgumentException("Page context and registry services are framework-owned.");
        }
    }

    private void Validate(IServiceCollection services)
    {
        RequireOwned(services, _binding);
        RequireOwned(services, _context);
        RequireOwned(services, _registry);
        foreach (Type type in _pages.Values.Select(page => page.PageType).OfType<Type>().Distinct())
        {
            ValidatePageService(services, type);
            if (!services.Any(descriptor => descriptor.ServiceType == type && !descriptor.IsKeyedService))
            {
                throw new ArgumentException("A registered page service was removed.");
            }
        }

        if (_initial is not null && (!_pages.TryGetValue(_initial.Value, out UiPageRegistration? initial) || initial.ArgumentsType != typeof(UiNoArgs)))
        {
            throw new ArgumentException("The initial page must be registered with no arguments.");
        }

        _snapshot = new(_pages, StringComparer.Ordinal);
        _serviceTypes = services.Select(service => service.ServiceType).Where(type => !type.ContainsGenericParameters).Distinct().ToArray();
    }
}
