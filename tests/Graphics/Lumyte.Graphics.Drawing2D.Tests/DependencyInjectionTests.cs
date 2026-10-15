using Lumyte.Graphics.Drawing2D.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Graphics.Drawing2D.Tests;

/// <summary>Checks fixed renderer selection, scoped ownership, and optional registration.</summary>
public sealed class DependencyInjectionTests
{
    /// <summary>Checks every default interface and registry without a graphics device.</summary>
    [Fact]
    public void GraphicsRegistrationResolvesEveryDefaultWithoutADevice()
    {
        var services = new ServiceCollection();
        Assert.Same(services, services.UseGraphics2D());
        Assert.Equal(4, services.Count);
        Assert.All(services, descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        using IServiceScope scope = provider.CreateScope();
        DrawingCommandRenderers2D renderers = scope.ServiceProvider.GetRequiredService<DrawingCommandRenderers2D>();

        Assert.IsType<DefaultFillRectRenderer2D>(renderers.FillRect);
        Assert.IsType<DefaultFillRoundedRectRenderer2D>(renderers.FillRoundedRect);
        Assert.IsType<DefaultFillCircleRenderer2D>(renderers.FillCircle);
        Assert.Same(scope.ServiceProvider.GetRequiredService<IFillRectRenderer2D>(), renderers.FillRect);
    }

    /// <summary>Checks that registry and implementations are shared only within their scope.</summary>
    [Fact]
    public void RegistriesAndDefaultsHaveScopedIdentity()
    {
        var services = new ServiceCollection();
        services.UseGraphics2D();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();
        DrawingCommandRenderers2D firstCommands = first.ServiceProvider.GetRequiredService<DrawingCommandRenderers2D>();
        DrawingCommandRenderers2D secondCommands = second.ServiceProvider.GetRequiredService<DrawingCommandRenderers2D>();

        Assert.Same(firstCommands, first.ServiceProvider.GetRequiredService<DrawingCommandRenderers2D>());
        Assert.NotSame(firstCommands, secondCommands);
    }

    /// <summary>Checks that replacements before or after default registration survive repeated calls.</summary>
    /// <param name="registerBeforeDefaults">Whether the replacements are registered first.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementsSurviveRepeatedRegistration(bool registerBeforeDefaults)
    {
        var services = new ServiceCollection();
        var fillRect = new DefaultFillRectRenderer2D();
        if (!registerBeforeDefaults)
        {
            services.UseGraphics2D();
        }

        services.AddScoped<IFillRectRenderer2D>(_ => fillRect);
        services.UseGraphics2D();
        int registrationCount = services.Count;
        services.UseGraphics2D();
        Assert.Equal(registrationCount, services.Count);

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        DrawingCommandRenderers2D renderers = scope.ServiceProvider.GetRequiredService<DrawingCommandRenderers2D>();

        Assert.Same(fillRect, renderers.FillRect);
    }

    /// <summary>Checks that registration does not resolve user factories and defaults are not used by DI.</summary>
    [Fact]
    public void RegistrationDefersResolutionAndPreservesRegistryOverrides()
    {
        var services = new ServiceCollection();
        var commands = DrawingCommandRenderers2D.CreateDefault();
        int resolutions = 0;
        services.AddScoped(_ =>
        {
            resolutions++;
            return commands;
        });
        services.UseGraphics2D();
        services.UseGraphics2D();
        Assert.Equal(0, resolutions);

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.Same(commands, scope.ServiceProvider.GetRequiredService<DrawingCommandRenderers2D>());
        Assert.Equal(1, resolutions);
    }

    /// <summary>Checks manual factories create independent registries and implementations without a provider.</summary>
    [Fact]
    public void DefaultFactoriesProduceIndependentInstances()
    {
        var firstCommands = DrawingCommandRenderers2D.CreateDefault();
        var secondCommands = DrawingCommandRenderers2D.CreateDefault();

        Assert.NotSame(firstCommands, secondCommands);
        Assert.NotSame(firstCommands.FillRect, secondCommands.FillRect);
        Assert.NotSame(firstCommands.FillRoundedRect, secondCommands.FillRoundedRect);
        Assert.NotSame(firstCommands.FillCircle, secondCommands.FillCircle);
    }

    /// <summary>Checks null collections fail before any registration work begins.</summary>
    [Fact]
    public void NullServiceCollectionsAreRejected()
        => Assert.Throws<ArgumentNullException>(() => Graphics2DServiceCollectionExtensions.UseGraphics2D(null!));
}
