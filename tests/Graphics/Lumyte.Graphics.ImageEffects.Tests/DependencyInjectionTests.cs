using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.ImageEffects.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Graphics.ImageEffects.Tests;

/// <summary>Checks effect composition without a drawing library or graphics device.</summary>
public sealed class DependencyInjectionTests
{
    /// <summary>Checks defaults and repeated registration without creating graphics services.</summary>
    [Fact]
    public void EffectsRegisterIndependentlyAndDeferConstruction()
    {
        var services = new ServiceCollection();
        Assert.Same(services, services.UseImageEffects());
        Assert.Same(services, services.UseImageEffects());
        Assert.Equal(4, services.Count);
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using IServiceScope scope = provider.CreateScope();
        Assert.IsType<DefaultColorMatrixEffectRenderer>(scope.ServiceProvider.GetRequiredService<IImageEffectRenderer<ColorMatrixEffect>>());
        Assert.IsType<DefaultGaussianBlurEffectRenderer>(scope.ServiceProvider.GetRequiredService<IImageEffectRenderer<GaussianBlurEffect>>());
        Assert.IsType<DefaultOpacityEffectRenderer>(scope.ServiceProvider.GetRequiredService<IImageEffectRenderer<OpacityEffect>>());
        Assert.IsType<DefaultCompositeEffectRenderer>(scope.ServiceProvider.GetRequiredService<IImageEffectRenderer<CompositeEffect>>());
        Assert.Null(scope.ServiceProvider.GetService<IGraphicDevice>());
        Assert.Null(scope.ServiceProvider.GetService<ImageEffectCompiler>());
    }

    /// <summary>Checks all typed renderers share their scope, but not another scope.</summary>
    [Fact]
    public void DefaultsHaveScopedIdentity()
    {
        var services = new ServiceCollection();
        services.UseImageEffects();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();
        AssertScope<IImageEffectRenderer<ColorMatrixEffect>>(first, second);
        AssertScope<IImageEffectRenderer<GaussianBlurEffect>>(first, second);
        AssertScope<IImageEffectRenderer<OpacityEffect>>(first, second);
        AssertScope<IImageEffectRenderer<CompositeEffect>>(first, second);
    }

    /// <summary>Checks replacement factories are deferred and survive repeated registration.</summary>
    /// <param name="registerBeforeDefaults">Whether the replacement precedes default registration.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReplacementsSurviveRepeatedRegistration(bool registerBeforeDefaults)
    {
        var services = new ServiceCollection();
        var opacity = new DefaultOpacityEffectRenderer();
        int resolutions = 0;
        if (!registerBeforeDefaults)
        {
            services.UseImageEffects();
        }

        services.AddScoped<IImageEffectRenderer<OpacityEffect>>(_ =>
        {
            resolutions++;
            return opacity;
        });
        services.UseImageEffects();
        int registrations = services.Count;
        services.UseImageEffects();
        Assert.Equal(registrations, services.Count);
        Assert.Equal(0, resolutions);
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope scope = provider.CreateScope();
        IImageEffectRenderer<OpacityEffect> resolved = scope.ServiceProvider.GetRequiredService<IImageEffectRenderer<OpacityEffect>>();
        Assert.Same(opacity, resolved);
        var definition = new OpacityEffect(0.5f);
        var effect = ImageEffectGraph.Create(definition, resolved, ImageEffectGraph.Source());
        Assert.Same(definition, Assert.IsType<EffectOperationNode>(effect.Root).Definition);
        Assert.Equal(1, resolutions);
        Assert.IsType<DefaultGaussianBlurEffectRenderer>(scope.ServiceProvider.GetRequiredService<IImageEffectRenderer<GaussianBlurEffect>>());
    }

    /// <summary>Checks convenience factories create independent effect graphs and rejects null DI collections.</summary>
    [Fact]
    public void DefaultFactoryAndNullCollectionContracts()
    {
        var a = ImageEffectGraph.Opacity(0.5f, ImageEffectGraph.Source());
        var b = ImageEffectGraph.Opacity(0.5f, ImageEffectGraph.Source());
        Assert.NotSame(a, b);
        Assert.IsType<OpacityEffect>(Assert.IsType<EffectOperationNode>(a.Root).Definition);
        Assert.Throws<ArgumentNullException>(() => ImageEffectsServiceCollectionExtensions.UseImageEffects(null!));
    }

    private static void AssertScope<T>(IServiceScope first, IServiceScope second)
        where T : class
    {
        T a = first.ServiceProvider.GetRequiredService<T>();
        Assert.Same(a, first.ServiceProvider.GetRequiredService<T>());
        Assert.NotSame(a, second.ServiceProvider.GetRequiredService<T>());
    }
}
