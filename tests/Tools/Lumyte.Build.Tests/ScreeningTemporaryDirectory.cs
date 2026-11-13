namespace Lumyte.Build.Tests;

internal sealed class ScreeningTemporaryDirectory : IDisposable
{
    public ScreeningTemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "screening 日本語 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
