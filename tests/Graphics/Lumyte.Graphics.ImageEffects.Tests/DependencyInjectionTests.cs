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
        Assert.Equal(5, services.Count);
        using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using IServiceScope scope = provider.CreateScope();
        ImageEffectRenderers effects = scope.ServiceProvider.GetRequiredService<ImageEffectRenderers>();
        Assert.IsType<DefaultColorMatrixEffectRenderer>(effects.ColorMatrix);
        Assert.IsType<DefaultGaussianBlurEffectRenderer>(effects.GaussianBlur);
        Assert.IsType<DefaultOpacityEffectRenderer>(effects.Opacity);
        Assert.IsType<DefaultCompositeEffectRenderer>(effects.Composite);
        Assert.Null(scope.ServiceProvider.GetService<IGraphicDevice>());
        Assert.Null(scope.ServiceProvider.GetService<ImageEffectCompiler>());
    }

    /// <summary>Checks all defaults and the table share their scope, but not another scope.</summary>
    [Fact]
    public void DefaultsHaveScopedIdentity()
    {
        var services = new ServiceCollection();
        services.UseImageEffects();
        using ServiceProvider provider = services.BuildServiceProvider();
        using IServiceScope first = provider.CreateScope();
        using IServiceScope second = provider.CreateScope();
        ImageEffectRenderers a = first.ServiceProvider.GetRequiredService<ImageEffectRenderers>();
        ImageEffectRenderers b = second.ServiceProvider.GetRequiredService<ImageEffectRenderers>();
        Assert.Same(a, first.ServiceProvider.GetRequiredService<ImageEffectRenderers>());
        Assert.Same(a.ColorMatrix, first.ServiceProvider.GetRequiredService<IColorMatrixEffectRenderer>());
        Assert.Same(a.GaussianBlur, first.ServiceProvider.GetRequiredService<IGaussianBlurEffectRenderer>());
        Assert.Same(a.Opacity, first.ServiceProvider.GetRequiredService<IOpacityEffectRenderer>());
        Assert.Same(a.Composite, first.ServiceProvider.GetRequiredService<ICompositeEffectRenderer>());
        Assert.NotSame(a, b);
        Assert.NotSame(a.ColorMatrix, b.ColorMatrix);
        Assert.NotSame(a.GaussianBlur, b.GaussianBlur);
        Assert.NotSame(a.Opacity, b.Opacity);
        Assert.NotSame(a.Composite, b.Composite);
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

        services.AddScoped<IOpacityEffectRenderer>(_ =>
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
        ImageEffectRenderers effects = scope.ServiceProvider.GetRequiredService<ImageEffectRenderers>();
        Assert.Same(opacity, effects.Opacity);
        Assert.Equal(1, resolutions);
        Assert.IsType<DefaultGaussianBlurEffectRenderer>(effects.GaussianBlur);
    }

    /// <summary>Checks manual composition creates independent defaults and rejects null DI collections.</summary>
    [Fact]
    public void DefaultFactoryAndNullCollectionContracts()
    {
        var a = ImageEffectRenderers.CreateDefault();
        var b = ImageEffectRenderers.CreateDefault();
        Assert.NotSame(a, b);
        Assert.NotSame(a.ColorMatrix, b.ColorMatrix);
        Assert.NotSame(a.GaussianBlur, b.GaussianBlur);
        Assert.NotSame(a.Opacity, b.Opacity);
        Assert.NotSame(a.Composite, b.Composite);
        Assert.Throws<ArgumentNullException>(() => ImageEffectsServiceCollectionExtensions.UseImageEffects(null!));
    }
}
