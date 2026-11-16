using Xunit;

namespace Lumyte.UI.WebView.Tests;

/// <summary>Scopes process-supervision cases to the implemented Linux backend.</summary>
internal sealed class CefHelperTheoryAttribute : TheoryAttribute
{
    /// <summary>Initializes a new instance of the <see cref="CefHelperTheoryAttribute"/> class.</summary>
    public CefHelperTheoryAttribute()
    {
        if (!OperatingSystem.IsLinux() || !File.Exists("/usr/bin/python3"))
        {
            Skip = "The optional helper supervisor requires Linux and Python3.";
        }
    }
}
