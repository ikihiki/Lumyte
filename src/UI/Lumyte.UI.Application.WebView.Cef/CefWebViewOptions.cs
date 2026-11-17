namespace Lumyte.UI.Application.WebView.Cef;

/// <summary>Explicit experimental CEF runtime configuration; not a production recommendation.</summary>
public sealed record CefWebViewOptions
{
    /// <summary>Gets the absolute optional helper path. Null leaves the browser unavailable.</summary>
    public string? HelperExecutablePath { get; init; }

    /// <summary>Gets the optional absolute native cleanup supervisor path. Null selects lumyte-cef-supervisor beside the helper.</summary>
    public string? SupervisorExecutablePath { get; init; }

    /// <summary>Gets a value indicating whether the helper sandbox may be explicitly disabled. Unsafe: trusted local content only; never enabled automatically.</summary>
    public bool AllowUnsandboxed { get; init; }

    /// <summary>Gets the finite native startup deadline.</summary>
    public TimeSpan StartupTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Gets the finite main-document deadline.</summary>
    public TimeSpan NavigationTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets the requested persistent profile. Non-null is unsupported and faults only the browser.</summary>
    public string? ProfileDirectory { get; init; }

    /// <summary>Gets a value indicating whether unavailable native IME must fault the browser session.</summary>
    public bool RequireIme { get; init; }

    /// <summary>Gets the maximum number of window-owned sessions. Only one is currently supported.</summary>
    public int MaximumSessions { get; init; } = 1;

    /// <summary>Gets the application CPU pixel and upload payload budget.</summary>
    public long CpuPixelBudgetBytes { get; init; } = 268435456;

    /// <summary>Gets the application GPU image payload budget.</summary>
    public long GpuImageBudgetBytes { get; init; } = 268435456;
}
