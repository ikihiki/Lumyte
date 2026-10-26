namespace Lumyte.Platform.Tests;

/// <summary>Runs native tests only on supported Windows hosts.</summary>
internal sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            Skip = "Requires Windows 10 version 1809 or later.";
        }
    }
}
