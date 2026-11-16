namespace Lumyte.UI.Application.WebView.Cef;

/// <summary>Explicit experimental CEF runtime configuration; not a production recommendation.</summary>
public sealed record CefWebViewOptions
{
    /// <summary>Gets the exclusively owned persistent profile, or null for memory-only storage.</summary>
    public string? ProfileDirectory { get; init; }

    /// <summary>Gets a value indicating whether unavailable native IME must reject startup.</summary>
    public bool RequireIme { get; init; }

    /// <summary>Gets the maximum number of window-owned sessions.</summary>
    public int MaximumSessions { get; init; } = 4;

    /// <summary>Gets the application CPU pixel and upload payload budget.</summary>
    public long CpuPixelBudgetBytes { get; init; } = 268435456;

    /// <summary>Gets the application GPU image payload budget.</summary>
    public long GpuImageBudgetBytes { get; init; } = 268435456;
}
