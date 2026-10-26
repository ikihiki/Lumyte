namespace Lumyte.Platform.Tests;

/// <summary>Runs X11 native tests when a Linux display is available.</summary>
internal sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            Skip = "Requires Linux and an X11 DISPLAY.";
        }
    }
}
