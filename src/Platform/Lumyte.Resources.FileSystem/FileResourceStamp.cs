using Microsoft.Win32.SafeHandles;

namespace Lumyte.Resources.FileSystem;

internal readonly record struct FileResourceStamp(long Length, DateTime CreationTime, DateTime LastWriteTime)
{
    internal string Revision => FormattableString.Invariant($"{Length:X16}:{CreationTime.Ticks:X16}:{LastWriteTime.Ticks:X16}");

    internal static FileResourceStamp Inspect(string path)
    {
        var information = new FileInfo(path);
        if (!information.Exists)
        {
            throw new FileNotFoundException("The resource does not exist or disappeared during reading.", path);
        }

        return new(information.Length, information.CreationTimeUtc, information.LastWriteTimeUtc);
    }

    internal void Verify(string path, FileStream input)
    {
        SafeFileHandle handle = input.SafeFileHandle;
        var opened = new FileResourceStamp(RandomAccess.GetLength(handle), File.GetCreationTimeUtc(handle), File.GetLastWriteTimeUtc(handle));
        if (this != opened || this != Inspect(path))
        {
            throw new IOException($"Resource '{path}' changed during direct reading. Reopen it and retry the operation.");
        }
    }
}
