using Lumyte.UI.Application;
using Lumyte.UI.Application.Diagnostics;
using Lumyte.UI.Application.Navigation.Diagnostics;
using Xunit;

namespace Lumyte.UI.Navigation.Tests;

/// <summary>Checks optional metadata registration remains separate from ordinary UI diagnostics.</summary>
public sealed class OptionalDiagnosticsTests
{
    /// <summary>Checks missing diagnostics and duplicate metadata registration are rejected during configuration.</summary>
    [Fact]
    public void NavigationMetadataRequiresBasicDiagnosticsAndCanBeRegisteredOnce()
    {
        UiApplicationBuilder builder = UiApplication.CreateBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.UseNavigationDiagnostics());
        builder.UseDiagnostics();
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(UiNavigationDiagnostics));
        builder.UseNavigationDiagnostics();
        Assert.Single(builder.Services, descriptor => descriptor.ServiceType == typeof(UiNavigationDiagnostics));
        Assert.Throws<InvalidOperationException>(() => builder.UseNavigationDiagnostics());
    }
}
