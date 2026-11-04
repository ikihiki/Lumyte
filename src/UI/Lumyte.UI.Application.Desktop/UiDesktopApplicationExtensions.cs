using Lumyte.Graphics.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Lumyte.UI.Application.Desktop;

/// <summary>Configures standard Windows or Linux desktop execution.</summary>
public static class UiDesktopApplicationExtensions
{
    /// <summary>Chooses a dedicated UI owner and the standard wgpu backend.</summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="options">Primary window defaults.</param>
    /// <returns>The builder.</returns>
    public static UiApplicationBuilder UseDesktop(this UiApplicationBuilder builder, UiDesktopApplicationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        options ??= new();
        ArgumentNullException.ThrowIfNull(options.Title);
        UiApplicationBuilder.ValidateSize(options.ClientSize);
        builder.SetPrimaryDefaults(options.Title, options.ClientSize);
        builder.UseDriver(() => new DesktopDriver());
        builder.Services.AddGraphicsDevice<DesktopDeviceFactory>(ServiceLifetime.Singleton);
        return builder;
    }
}
