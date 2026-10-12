using Lumyte.Graphics.Drawing2D.DependencyInjection;
using Lumyte.Graphics.ImageEffects;
using Lumyte.Graphics.ImageEffects.DependencyInjection;
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
        Assert.Equal(17, services.Count);
        Assert.All(services, descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));

        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
        using IServiceScope scope = provider.CreateScope();
        DrawingCommandRenderers2D renderers = scope.ServiceProvider.GetRequiredService<DrawingCommandRenderers2D>();
        ImageEffectRenderers effects = scope.ServiceProvider.GetRequiredService<ImageEffectRenderers>();

        Assert.IsType<DefaultFillRectRenderer2D>(renderers.FillRect);
        Assert.IsType<DefaultFillRoundedRectRenderer2D>(renderers.FillRoundedRect);
        Assert.IsType<DefaultFillCircleRenderer2D>(renderers.FillCircle);
        Assert.IsType<DefaultDrawLineRenderer2D>(renderers.DrawLine);
        Assert.IsType<DefaultFillPathRenderer2D>(renderers.FillPath);
        Assert.IsType<DefaultStrokePathRenderer2D>(renderers.StrokePath);
        Assert.IsType<DefaultDrawImageRenderer2D>(renderers.DrawImage);
        Assert.IsType<DefaultDrawCoverageMaskRenderer2D>(renderers.DrawCoverageMask);
        Assert.IsType<DefaultDrawDistanceFieldRenderer2D>(renderers.DrawDistanceField);
        Assert.IsType<DefaultDrawMeshRenderer2D>(renderers.DrawMesh);
        Assert.IsType<DefaultDrawPaintGraphRenderer2D>(renderers.DrawPaintGraph);
        Assert.IsType<DefaultColorMatrixEffectRenderer>(effects.ColorMatrix);
        Assert.IsType<DefaultGaussianBlurEffectRenderer>(effects.GaussianBlur);
        Assert.IsType<DefaultOpacityEffectRenderer>(effects.Opacity);
        Assert.IsType<DefaultCompositeEffectRenderer>(effects.Composite);
        Assert.Same(scope.ServiceProvider.GetRequiredService<IFillPathRenderer2D>(), renderers.FillPath);
        Assert.Same(scope.ServiceProvider.GetRequiredService<IOpacityEffectRenderer>(), effects.Opacity);
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
        ImageEffectRenderers firstEffects = first.ServiceProvider.GetRequiredService<ImageEffectRenderers>();
        ImageEffectRenderers secondEffects = second.ServiceProvider.GetRequiredService<ImageEffectRenderers>();

        Assert.Same(firstCommands, first.ServiceProvider.GetRequiredService<DrawingCommandRenderers2D>());
        Assert.Same(firstEffects, first.ServiceProvider.GetRequiredService<ImageEffectRenderers>());
        Assert.NotSame(firstCommands, secondCommands);
        Assert.NotSame(firstCommands.FillPath, secondCommands.FillPath);
        Assert.NotSame(firstEffects, secondEffects);
        Assert.NotSame(firstEffects.Opacity, secondEffects.Opacity);
    }

    /// <summary>Checks that replacements before or after default registration survive repeated calls.</summary>
    /// <param name="registerBeforeDefaults">Whether the replacements are registered first.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementsSurviveRepeatedRegistration(bool registerBeforeDefaults)
    {
        var services = new ServiceCollection();
        var fillPath = new DefaultFillPathRenderer2D();
        var opacity = new DefaultOpacityEffectRenderer();
        if (!registerBeforeDefaults)
        {
            services.UseGraphics2D();
        }

        services.AddScoped<IFillPathRenderer2D>(_ => fillPath);
        services.AddScoped<IOpacityEffectRenderer>(_ => opacity);
        services.UseGraphics2D();
        int registrationCount = services.Count;
        services.UseGraphics2D();
        services.UseImageEffects();
        Assert.Equal(registrationCount, services.Count);

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        DrawingCommandRenderers2D renderers = scope.ServiceProvider.GetRequiredService<DrawingCommandRenderers2D>();
        ImageEffectRenderers effects = scope.ServiceProvider.GetRequiredService<ImageEffectRenderers>();

        Assert.Same(fillPath, renderers.FillPath);
        Assert.Same(opacity, effects.Opacity);
        Assert.IsType<DefaultStrokePathRenderer2D>(renderers.StrokePath);
        Assert.IsType<DefaultDrawPaintGraphRenderer2D>(renderers.DrawPaintGraph);
        Assert.IsType<DefaultGaussianBlurEffectRenderer>(effects.GaussianBlur);
    }

    /// <summary>Checks that registration does not resolve user factories and defaults are not used by DI.</summary>
    [Fact]
    public void RegistrationDefersResolutionAndPreservesRegistryOverrides()
    {
        var services = new ServiceCollection();
        var commands = DrawingCommandRenderers2D.CreateDefault();
        var effects = ImageEffectRenderers.CreateDefault();
        int resolutions = 0;
        services.AddScoped(_ =>
        {
            resolutions++;
            return commands;
        });
        services.AddScoped(_ =>
        {
            resolutions++;
            return effects;
        });
        services.UseGraphics2D();
        services.UseGraphics2D();
        Assert.Equal(0, resolutions);

        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        Assert.Same(commands, scope.ServiceProvider.GetRequiredService<DrawingCommandRenderers2D>());
        Assert.Same(effects, scope.ServiceProvider.GetRequiredService<ImageEffectRenderers>());
        Assert.Equal(2, resolutions);
    }

    /// <summary>Checks manual factories create independent registries and implementations without a provider.</summary>
    [Fact]
    public void DefaultFactoriesProduceIndependentInstances()
    {
        var firstCommands = DrawingCommandRenderers2D.CreateDefault();
        var secondCommands = DrawingCommandRenderers2D.CreateDefault();
        var firstEffects = ImageEffectRenderers.CreateDefault();
        var secondEffects = ImageEffectRenderers.CreateDefault();

        Assert.NotSame(firstCommands, secondCommands);
        Assert.NotSame(firstCommands.FillRect, secondCommands.FillRect);
        Assert.NotSame(firstCommands.FillRoundedRect, secondCommands.FillRoundedRect);
        Assert.NotSame(firstCommands.FillCircle, secondCommands.FillCircle);
        Assert.NotSame(firstCommands.DrawLine, secondCommands.DrawLine);
        Assert.NotSame(firstCommands.FillPath, secondCommands.FillPath);
        Assert.NotSame(firstCommands.StrokePath, secondCommands.StrokePath);
        Assert.NotSame(firstCommands.DrawImage, secondCommands.DrawImage);
        Assert.NotSame(firstCommands.DrawCoverageMask, secondCommands.DrawCoverageMask);
        Assert.NotSame(firstCommands.DrawDistanceField, secondCommands.DrawDistanceField);
        Assert.NotSame(firstCommands.DrawMesh, secondCommands.DrawMesh);
        Assert.NotSame(firstCommands.DrawPaintGraph, secondCommands.DrawPaintGraph);
        Assert.NotSame(firstEffects, secondEffects);
        Assert.NotSame(firstEffects.ColorMatrix, secondEffects.ColorMatrix);
        Assert.NotSame(firstEffects.GaussianBlur, secondEffects.GaussianBlur);
        Assert.NotSame(firstEffects.Opacity, secondEffects.Opacity);
        Assert.NotSame(firstEffects.Composite, secondEffects.Composite);
    }

    /// <summary>Checks null collections fail before any registration work begins.</summary>
    [Fact]
    public void NullServiceCollectionsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Graphics2DServiceCollectionExtensions.UseGraphics2D(null!));
        Assert.Throws<ArgumentNullException>(() => ImageEffectsServiceCollectionExtensions.UseImageEffects(null!));
    }
}
