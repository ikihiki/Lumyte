using System.Diagnostics.CodeAnalysis;
using Lumyte.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Navigation;

/// <summary>Registers immutable routes and DI page factories before the application is built.</summary>
public static class UiPageRegistrationExtensions
{
    /// <summary>Registers a no-argument page as one scoped service.</summary>
    /// <typeparam name="TPage">The page's concrete service type.</typeparam>
    /// <param name="builder">The mutable application builder.</param>
    /// <param name="key">The canonical route.</param>
    /// <returns>The typed destination key.</returns>
    public static UiPageKey<UiNoArgs> MapPage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPage>(this UiApplicationBuilder builder, string key)
        where TPage : class, IUiPage => builder.MapPage<TPage, UiNoArgs>(new(key), static arguments => arguments);

    /// <summary>Registers a typed page and a pure argument copier.</summary>
    /// <typeparam name="TPage">The page's concrete scoped service type.</typeparam>
    /// <typeparam name="TArgs">The exact argument type.</typeparam>
    /// <param name="builder">The mutable application builder.</param>
    /// <param name="key">The canonical route and argument type.</param>
    /// <param name="captureArgs">Copies arguments for history and each read.</param>
    /// <returns>The registered key.</returns>
    public static UiPageKey<TArgs> MapPage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPage, TArgs>(this UiApplicationBuilder builder, UiPageKey<TArgs> key, Func<TArgs, TArgs> captureArgs)
        where TPage : class, IUiPage
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.CheckMutable();
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(captureArgs);
        UiPageRegistrationState.ValidatePageService(builder.Services, typeof(TPage));
        var state = UiPageRegistrationState.Get(builder);
        state.CheckRoute(key.Value);
        if (!builder.Services.Any(descriptor => descriptor.ServiceType == typeof(TPage) && !descriptor.IsKeyedService))
        {
            builder.Services.AddScoped<TPage>();
        }

        state.Add(new UiPageRegistration<TArgs>(key, captureArgs, static services => services.GetRequiredService<TPage>(), typeof(TPage)));
        return key;
    }

    /// <summary>Creates a passive route prefix without creating scopes or pages.</summary>
    /// <param name="builder">The mutable application builder.</param>
    /// <param name="prefix">The canonical prefix.</param>
    /// <returns>The prefix group.</returns>
    public static UiPageGroupBuilder MapGroup(this UiApplicationBuilder builder, string prefix)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.CheckMutable();
        return new(builder, new UiPageKey<UiNoArgs>(prefix).Value);
    }

    /// <summary>Starts registered DI pages in the primary window.</summary>
    /// <param name="builder">The mutable application builder.</param>
    /// <param name="initialKey">A registered no-argument initial route.</param>
    /// <param name="options">The copied navigation limits.</param>
    /// <param name="onCompleted">The result callback on the UI owner.</param>
    /// <returns>The builder.</returns>
    public static UiApplicationBuilder UseNavigation(this UiApplicationBuilder builder, string initialKey, UiNavigationOptions? options = null, Action<UiNavigationResult>? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var key = new UiPageKey<UiNoArgs>(initialKey);
        Func<IServiceProvider, IUiApplicationContent> content = UiPageWindows.Page(key, options, onCompleted);
        UiPageRegistrationState.Get(builder).UseInitial(key, content);
        return builder;
    }

    /// <summary>Registers a synchronous page handler using one activation scope.</summary>
    /// <param name="builder">The mutable application builder.</param>
    /// <param name="key">The canonical route.</param>
    /// <param name="handler">Builds one disconnected root on the UI owner.</param>
    /// <returns>The typed destination key.</returns>
    public static UiPageKey<UiNoArgs> MapPage(this UiApplicationBuilder builder, string key, Func<UiPageContext, Widget> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RegisterInline(builder, key, _ => new InlinePage(handler));
    }

    /// <summary>Registers a synchronous page handler using one activation scope.</summary>
    /// <typeparam name="T1">The first service resolved from the page scope.</typeparam>
    /// <param name="builder">The mutable application builder.</param>
    /// <param name="key">The canonical route.</param>
    /// <param name="handler">Builds one disconnected root on the UI owner.</param>
    /// <returns>The typed destination key.</returns>
    public static UiPageKey<UiNoArgs> MapPage<T1>(this UiApplicationBuilder builder, string key, Func<UiPageContext, T1, Widget> handler)
        where T1 : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RegisterInline(builder, key, services =>
        {
            T1 service1 = services.GetRequiredService<T1>();
            return new InlinePage(context => handler(context, service1));
        });
    }

    /// <summary>Registers a synchronous page handler using one activation scope.</summary>
    /// <typeparam name="T1">The first service resolved from the page scope.</typeparam>
    /// <typeparam name="T2">The second service resolved from the page scope.</typeparam>
    /// <param name="builder">The mutable application builder.</param>
    /// <param name="key">The canonical route.</param>
    /// <param name="handler">Builds one disconnected root on the UI owner.</param>
    /// <returns>The typed destination key.</returns>
    public static UiPageKey<UiNoArgs> MapPage<T1, T2>(this UiApplicationBuilder builder, string key, Func<UiPageContext, T1, T2, Widget> handler)
        where T1 : notnull
        where T2 : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RegisterInline(builder, key, services =>
        {
            T1 service1 = services.GetRequiredService<T1>();
            T2 service2 = services.GetRequiredService<T2>();
            return new InlinePage(context => handler(context, service1, service2));
        });
    }

    /// <summary>Registers a synchronous page handler using one activation scope.</summary>
    /// <typeparam name="T1">The first service resolved from the page scope.</typeparam>
    /// <typeparam name="T2">The second service resolved from the page scope.</typeparam>
    /// <typeparam name="T3">The third service resolved from the page scope.</typeparam>
    /// <param name="builder">The mutable application builder.</param>
    /// <param name="key">The canonical route.</param>
    /// <param name="handler">Builds one disconnected root on the UI owner.</param>
    /// <returns>The typed destination key.</returns>
    public static UiPageKey<UiNoArgs> MapPage<T1, T2, T3>(this UiApplicationBuilder builder, string key, Func<UiPageContext, T1, T2, T3, Widget> handler)
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
    {
        ArgumentNullException.ThrowIfNull(handler);
        return RegisterInline(builder, key, services =>
        {
            T1 service1 = services.GetRequiredService<T1>();
            T2 service2 = services.GetRequiredService<T2>();
            T3 service3 = services.GetRequiredService<T3>();
            return new InlinePage(context => handler(context, service1, service2, service3));
        });
    }

    private static UiPageKey<UiNoArgs> RegisterInline(UiApplicationBuilder builder, string route, Func<IServiceProvider, IUiPage> create)
    {
        var key = new UiPageKey<UiNoArgs>(route);
        var state = UiPageRegistrationState.Get(builder);
        state.CheckRoute(key.Value);
        state.Add(new UiPageRegistration<UiNoArgs>(key, static value => value, create, null));
        return key;
    }

    private sealed class InlinePage(Func<UiPageContext, Widget> build) : IUiPage
    {
        public Widget Build(UiPageContext context) => build(context);
    }
}
