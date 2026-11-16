using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Requires the Linux-owned-process supervisor, optionally a real native helper.</summary>
internal sealed class CefHelperFactAttribute : FactAttribute
{
    /// <summary>Initializes a new instance of the <see cref="CefHelperFactAttribute"/> class.</summary>
    /// <param name="requireNative">Whether native CEF qualification was explicitly requested.</param>
    public CefHelperFactAttribute(bool requireNative = false)
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/python3"))
        {
            Skip = "The optional helper supervisor requires Linux and Python3.";
        }
        else if (requireNative && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("LUMYTE_TEST_CEF_HELPER")))
        {
            Skip = "Set LUMYTE_TEST_CEF_HELPER to explicitly qualify native helper failure isolation.";
        }
    }
}
