namespace Lumyte.Samples.Framework;

/// <summary>A runnable backend adapter. Project paths are relative to the repository root.</summary>
/// <param name="Id">Stable command-line identifier.</param>
/// <param name="Project">Project to run or publish.</param>
/// <param name="Arguments">Arguments prepended to the sample arguments.</param>
/// <param name="Browser">Whether to publish and serve a WebAssembly application.</param>
/// <param name="WindowsOnly">Whether this adapter requires Windows.</param>
public sealed record SampleBackend(string Id, string Project, string[] Arguments, bool Browser = false, bool WindowsOnly = false)
{
    /// <summary>Gets a value indicating whether the current host OS supports this adapter; native dependencies are checked at launch.</summary>
    public bool IsAvailable => !WindowsOnly || OperatingSystem.IsWindows();
}
