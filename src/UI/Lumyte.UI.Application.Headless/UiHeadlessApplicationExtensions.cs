namespace Lumyte.UI.Application.Headless;

/// <summary>Registers the CPU-only driver without OS or GPU capabilities.</summary>
public static class UiHeadlessApplicationExtensions
{
    /// <summary>Chooses headless execution on a dedicated owner thread.</summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="options">Virtual geometry.</param>
    /// <returns>The builder.</returns>
    public static UiApplicationBuilder UseHeadless(this UiApplicationBuilder builder, UiHeadlessApplicationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        options ??= new();
        UiApplicationBuilder.ValidateSize(options.ClientSize);
        UiApplicationBuilder.ValidateSize(options.Scale);
        builder.SetPrimaryDefaults("Lumyte", options.ClientSize);
        return builder.UseDriver(() => new HeadlessDriver(options));
    }
}
