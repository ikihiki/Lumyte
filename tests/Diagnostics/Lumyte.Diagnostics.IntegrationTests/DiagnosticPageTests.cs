using Lumyte.Diagnostics.Server;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lumyte.Diagnostics.IntegrationTests;

/// <summary>Checks extension definitions before they enter authenticated Circuits.</summary>
public sealed class DiagnosticPageTests
{
    /// <summary>Checks ambiguous route definitions are rejected.</summary>
    [Fact]
    public void RejectsDuplicateIds()
        => Assert.Throws<ArgumentException>(() => new DiagnosticPageRegistry([new("physics", "Physics", "Engine", 1), new("physics", "Other", "Engine", 2)]));

    /// <summary>Checks URL segments and component boundaries.</summary>
    [Fact]
    public void RejectsInvalidDefinitions()
    {
        Assert.Throws<ArgumentException>(() => new DiagnosticPageRegistry([new("../physics", "Physics", "Engine", 1)]));
        Assert.Throws<ArgumentException>(() => new DiagnosticPageRegistry([new("physics", "Physics", "Engine", 1, Component: typeof(string))]));
        Assert.Throws<ArgumentException>(() => new DiagnosticPageRegistry([new("physics", "Physics", "Engine", 1, Component: typeof(ComponentBase))]));
    }

    /// <summary>Checks DI definitions retain component identity and subsystem requirements.</summary>
    [Fact]
    public void OrdersExtensionPages()
    {
        var registry = new DiagnosticPageRegistry([new("second", "Second", "Engine", 2), new("physics", "Physics", "Engine", 1, "physics", typeof(TestPage))]);
        Assert.Equal("physics", registry.Pages[0].Id);
        Assert.Equal(typeof(TestPage), registry.Pages[0].Component);
        Assert.Equal("physics", registry.Pages[0].RequiredSubsystem);
    }

    /// <summary>Checks host registrations extend the built-in catalog without shell changes.</summary>
    /// <returns>The test completion.</returns>
    [Fact]
    public async Task RegistersHostExtensionAsync()
    {
        await using WebApplication app = DiagnosticServerApplication.Create(
            [],
            options =>
            {
                options.GameToken = "test-game";
                options.OperatorToken = "test-operator";
                options.HttpPort = 0;
                options.MagicOnionPort = 0;
            },
            services => services.AddSingleton(new DiagnosticPageDefinition("physics", "Physics", "Engine", 5, "physics", typeof(TestPage))));
        DiagnosticPageRegistry registry = app.Services.GetRequiredService<DiagnosticPageRegistry>();
        Assert.Contains(registry.Pages, page => page.Id == "physics" && page.Component == typeof(TestPage));
        Assert.Contains(registry.Pages, page => page.Id == "resources");
    }

    private sealed class TestPage : ComponentBase;
}
