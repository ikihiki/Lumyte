using Lumyte.UI.Application.Headless;
using Lumyte.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Navigation.Tests;

/// <summary>Checks typed handler invocation and context binding through real navigation.</summary>
public sealed class InlineTests
{
    /// <summary>Checks zero to three services are resolved in the same scope and handlers run once on the owner.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task InlineAritiesUseIndependentScopesAndRunOnceOnTheOwnerAsync()
    {
        var probe = new PageProbe();
        var contexts = new List<UiPageContext>();
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.Services.AddSingleton(probe);
        builder.Services.AddScoped<PageService>();
        Widget Build(UiPageContext context)
        {
            Assert.True(context.Activation.Dispatcher.CheckAccess());
            contexts.Add(context);
            return Panel();
        }

        builder.MapPage("/", Build);
        builder.MapPage<PageService>("/one", (context, first) =>
        {
            Assert.Same(context, first.Context);
            return Build(context);
        });
        builder.MapPage<PageService, PageService>("/two", (context, first, second) =>
        {
            Assert.Same(first, second);
            return Build(context);
        });
        builder.MapGroup("/group").MapPage<PageService, PageService, PageService>("/three", (context, first, second, third) =>
        {
            Assert.Same(first, second);
            Assert.Same(second, third);
            return Build(context);
        });
        builder.UseNavigation("/");
        await using UiApplication app = builder.Build();
        Assert.Empty(contexts);
        Assert.Empty(probe.Services);
        Task running = app.RunAsync();
        await WaitAsync(app, () => contexts.Count == 1 && contexts[0].Activation.Navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        IUiNavigator navigation = contexts[0].Activation.Navigation;
        foreach (string route in new[] { "/one", "/two", "/group/three" })
        {
            UiNavigationRequest request = await app.Dispatcher.InvokeAsync(() => navigation.RequestNavigate(route));
            Assert.Equal(UiNavigationStatus.Committed, (await request.Completion.WaitAsync(TimeSpan.FromSeconds(10))).Status);
            await WaitAsync(app, () => navigation.CaptureSnapshot().Phase == UiNavigationPhase.Idle);
        }

        Assert.Equal(4, contexts.Count);
        Assert.Equal(3, probe.Services.Count);
        Assert.Equal(3, probe.Services.Distinct().Count());
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await running;
        Assert.All(probe.Services, service => Assert.Equal(1, service.Disposes));
    }

    /// <summary>Checks root and unbound independent scopes cannot construct registered pages.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task ContextIsBoundBeforeConstructorAndUnavailableFromUnboundScopesAsync()
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless();
        UiPageKey<UiNoArgs> key = builder.MapPage<ContextPage>("/");
        builder.UseContent(services =>
        {
            Assert.Throws<InvalidOperationException>(() => services.GetRequiredService<ContextPage>());
            using IServiceScope scope = services.CreateScope();
            Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<UiPageContext>());
            Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<ContextPage>());
            return UiPageWindows.Page(key)(services);
        });
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        await WaitAsync(app, () => app.State == UiApplicationState.Running);
        await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await running;
    }

    internal static async Task WaitAsync(UiApplication app, Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (app.State == UiApplicationState.Starting)
        {
            if (app.Completion.IsCompleted)
            {
                await app.Completion;
            }

            await Task.Delay(1, timeout.Token);
        }

        while (!await app.Dispatcher.InvokeAsync(condition, timeout.Token))
        {
            Assert.Null(app.Failure);
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class ContextPage(UiPageContext context) : IUiPage
    {
        public Widget Build(UiPageContext current)
        {
            Assert.Same(context, current);
            return Panel();
        }
    }
}
