using Lumyte.UI.Application.Headless;
using Lumyte.UI.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Application.Navigation.Tests;

/// <summary>Checks route registration is passive, atomic and frozen with application services.</summary>
public sealed class RegistrationTests
{
    /// <summary>Checks existing scoped factories are not run at registration or Build and group names stay canonical.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task RegistrationIsPassiveAndFreezesGroupsAndServicesAtBuildAsync()
    {
        int calls = 0;
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless();
        builder.Services.AddScoped<EmptyPage>(_ =>
        {
            calls++;
            return new();
        });
        UiPageGroupBuilder group = builder.MapGroup("/settings").MapGroup("/advanced");
        UiPageKey<UiNoArgs> first = group.MapPage<EmptyPage>("/");
        Assert.Equal("/settings/advanced", first.Value);
        Assert.Equal("/settings/advanced/other", group.MapPage<EmptyPage>("/other").Value);
        Assert.Single(builder.Services, item => item.ServiceType == typeof(EmptyPage));
        Assert.DoesNotContain(builder.Services, item => item.ServiceType == typeof(IUiPage));
        builder.UseNavigation(first.Value);
        await using UiApplication app = builder.Build();
        Assert.Equal(0, calls);
        Assert.True(builder.Services.IsReadOnly);
        Assert.Throws<InvalidOperationException>(() => group.MapPage<EmptyPage>("/late"));
        Assert.Throws<InvalidOperationException>(() => group.MapGroup("/late"));
        Assert.Throws<InvalidOperationException>(() => builder.MapPage<EmptyPage>("/late"));
    }

    /// <summary>Checks duplicate routes do not add page services and invalid routes do not initialize registration infrastructure.</summary>
    [Fact]
    public void InvalidAndDuplicateEntriesLeaveExistingRegistrationsUnchanged()
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder();
        Assert.Throws<ArgumentException>(() => builder.MapPage<EmptyPage>("bad"));
        Assert.Empty(builder.Services);
        builder.MapPage("/", _ => Panel());
        int count = builder.Services.Count;
        Assert.Throws<ArgumentException>(() => builder.MapPage<EmptyPage>("/"));
        Assert.Equal(count, builder.Services.Count);
        Assert.Throws<ArgumentNullException>(() => builder.MapPage<EmptyPage, int>(new UiPageKey<int>("/typed"), null!));
        Assert.Equal(count, builder.Services.Count);
        Assert.Throws<ArgumentException>(() => builder.MapGroup("/Upper"));
        Assert.Throws<ArgumentException>(() => builder.MapGroup("/group").MapPage("/../bad", _ => Panel()));
        Assert.Equal(count, builder.Services.Count);
    }

    /// <summary>Checks singleton, transient and duplicate service registrations cannot own page scopes.</summary>
    /// <param name="kind">The invalid service registration.</param>
    [Theory]
    [InlineData("singleton")]
    [InlineData("transient")]
    [InlineData("instance")]
    [InlineData("duplicate")]
    public void PageServiceMustBeOneUnkeyedScopedRegistration(string kind)
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder();
        switch (kind)
        {
            case "singleton":
                builder.Services.AddSingleton<EmptyPage>();
                break;
            case "transient":
                builder.Services.AddTransient<EmptyPage>();
                break;
            case "instance":
                builder.Services.AddSingleton(new EmptyPage());
                break;
            default:
                builder.Services.AddScoped<EmptyPage>();
                builder.Services.AddScoped<EmptyPage>();
                break;
        }

        int count = builder.Services.Count;
        Assert.Throws<ArgumentException>(() => builder.MapPage<EmptyPage>("/"));
        Assert.Equal(count, builder.Services.Count);
    }

    /// <summary>Checks Build validates later service changes and that failure does not freeze a repairable builder.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task BuildRejectsContextReplacementAndAllowsRepairBeforeSuccessfulFreezeAsync()
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless();
        builder.MapPage<EmptyPage>("/");
        builder.UseNavigation("/");
        var replacement = ServiceDescriptor.Scoped<UiPageContext>(_ => null!);
        builder.Services.Add(replacement);
        Assert.Throws<ArgumentException>(() => builder.Build());
        Assert.False(builder.Services.IsReadOnly);
        builder.Services.Remove(replacement);
        await using UiApplication app = builder.Build();
    }

    /// <summary>Checks initial route validation occurs at Build without running a page factory.</summary>
    /// <param name="typed">Whether the route has a mismatched argument type rather than being absent.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialPageMustResolveAsNoArguments(bool typed)
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseHeadless();
        builder.UseNavigation("/initial");
        if (typed)
        {
            builder.MapPage<EmptyPage, int>(new("/initial"), static value => value);
        }

        Assert.Throws<ArgumentException>(() => builder.Build());
        Assert.Throws<InvalidOperationException>(() => builder.UseNavigation("/initial"));
    }

    /// <summary>Checks navigation shares the standard content slot and rejects invalid budgets.</summary>
    [Fact]
    public void ContentSourcesAndHistoryBudgetsAreValidated()
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder().UseRoot(_ => Panel());
        Assert.Throws<ArgumentException>(() => builder.UseNavigation("/"));
        Assert.Throws<ArgumentOutOfRangeException>(() => UiPageWindows.Page(new UiPageKey<UiNoArgs>("/"), new UiNavigationOptions { MaxHistoryEntries = 0 }));
        Assert.Equal("/child", UiApplication.CreateBuilder().MapGroup("/").MapGroup("/child").Prefix);
    }

    private sealed class EmptyPage : IUiPage
    {
        public Widget Build(UiPageContext context) => Panel();
    }
}
