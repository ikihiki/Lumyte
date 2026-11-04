using System.Runtime.Versioning;
using Lumyte.Graphics.Hosting;
using Microsoft.Extensions.DependencyInjection;

[assembly: SupportedOSPlatform("browser")]

namespace Lumyte.UI.Application.Browser;

/// <summary>Configures a current-JS-thread application with WebGPU rendering.</summary>
public static class UiBrowserApplicationExtensions
{
    /// <summary>Chooses the browser driver without opening a browser window or changing DOM ownership.</summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="options">The canvas and bridge URL.</param>
    /// <returns>The builder.</returns>
    public static UiApplicationBuilder UseBrowser(this UiApplicationBuilder builder, UiBrowserApplicationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        options ??= new();
        ArgumentException.ThrowIfNullOrWhiteSpace(options.CanvasId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModulePath);
        builder.UseDriver(() => new BrowserDriver(options));
        builder.Services.AddGraphicsDevice<BrowserDeviceFactory>(ServiceLifetime.Singleton);
        return builder;
    }
}
