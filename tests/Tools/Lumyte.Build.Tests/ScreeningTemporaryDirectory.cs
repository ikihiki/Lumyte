namespace Lumyte.Build.Tests;

internal sealed class ScreeningTemporaryDirectory : IDisposable
{
    public ScreeningTemporaryDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "screening 日本語 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        foreach (string file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
        {
            FileAttributes attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(Path, recursive: true);
    }
}
