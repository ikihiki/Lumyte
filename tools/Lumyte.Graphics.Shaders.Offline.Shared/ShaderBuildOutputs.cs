using System.Text;
using System.Text.Json;

namespace Lumyte.Graphics.Shaders.Offline;

/// <summary>Publishes one successful offline build and removes only its previously recorded outputs.</summary>
internal static class ShaderBuildOutputs
{
    private const string Inventory = "generated-files.json";
    private const string Sources = "generated-sources.txt";
    private const string Resources = "generated-resources.txt";
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    internal static async Task PublishAsync(string outputDirectory, IReadOnlyDictionary<string, byte[]> outputs,
        CancellationToken token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentNullException.ThrowIfNull(outputs);
        token.ThrowIfCancellationRequested();
        string directory = Path.GetFullPath(outputDirectory);
        RejectLink(directory);
        var files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var destinations = new HashSet<string>(PathComparer);
        foreach ((string name, byte[] data) in outputs)
        {
            string relative;
            string path;
            try
            {
                relative = ValidateName(name);
                path = Resolve(directory, relative);
            }
            catch (ArgumentException error)
            { throw new ArgumentException(error.Message, nameof(outputs), error); }
            if (data is null || !destinations.Add(path))
            { throw new ArgumentException("Outputs require non-null bytes and distinct destination paths.", nameof(outputs)); }
            files.Add(relative, data);
        }

        string inventoryPath = Resolve(directory, Inventory);
        string[] previous = [];
        var previousPaths = new HashSet<string>(PathComparer);
        if (File.Exists(inventoryPath))
        {
            try
            {
                previous = JsonSerializer.Deserialize<string[]>(await File.ReadAllBytesAsync(inventoryPath, token))
                    ?? throw new InvalidDataException("The generated output inventory cannot be null.");
                previous = previous.Select(ValidateName).ToArray();
                foreach (string name in previous)
                {
                    if (!previousPaths.Add(Resolve(directory, name)))
                    { throw new InvalidDataException("The generated output inventory contains duplicate paths."); }
                }
            }
            catch (Exception error) when (error is JsonException or ArgumentException)
            { throw new InvalidDataException("The generated output inventory contains invalid paths or data.", error); }
        }

        var changes = new List<Change>();
        foreach ((string name, byte[] bytes) in files)
        {
            string path = Resolve(directory, name);
            if (File.Exists(path) && !previousPaths.Contains(path))
            { throw new IOException("A generated output would replace a file absent from its ownership inventory: " + path); }
            await AddChangeAsync(path, bytes, changes, token);
        }
        foreach (string name in previous)
        {
            string path = Resolve(directory, name);
            if (!destinations.Contains(path))
            {
                if (Directory.Exists(path)) { throw new IOException("A previous generated file is now a directory: " + path); }
                if (File.Exists(path)) { changes.Add(new(path, null)); }
            }
        }
        if (!File.Exists(inventoryPath))
        {
            foreach (string name in new[] { Sources, Resources })
            {
                string path = Resolve(directory, name);
                if (File.Exists(path)) { throw new IOException("A generated input list exists without its ownership inventory: " + path); }
            }
        }
        await AddChangeAsync(inventoryPath, JsonSerializer.SerializeToUtf8Bytes(files.Keys), changes, token);
        await AddChangeAsync(Resolve(directory, Sources), InputList(directory, files.Keys, ".cs"), changes, token);
        await AddChangeAsync(Resolve(directory, Resources), InputList(directory, files.Keys, ".resources.xml"), changes, token);
        if (changes.Count == 0) { return; }

        Directory.CreateDirectory(directory);
        string staging = Path.Combine(directory, ".publish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        bool preserveBackups = false;
        try
        {
            for (int index = 0; index < changes.Count; index++)
            {
                Change change = changes[index];
                Directory.CreateDirectory(Path.GetDirectoryName(change.Path)!);
                RejectLink(Path.GetDirectoryName(change.Path)!);
                if (change.Bytes is not null)
                {
                    change.Staged = Path.Combine(staging, index + ".new");
                    await File.WriteAllBytesAsync(change.Staged, change.Bytes, token);
                }
            }
            token.ThrowIfCancellationRequested();
            var applied = new List<Change>(changes.Count);
            try
            {
                // Cancellation is observed before committing. Once publication starts, finish or roll it back.
                for (int index = 0; index < changes.Count; index++)
                {
                    Change change = changes[index];
                    RejectLink(change.Path);
                    if (File.Exists(change.Path))
                    {
                        string backup = Path.Combine(staging, index + ".old");
                        File.Move(change.Path, backup);
                        change.Backup = backup;
                    }
                    applied.Add(change);
                    if (change.Staged is not null) { File.Move(change.Staged, change.Path); }
                }
            }
            catch (Exception publicationError)
            {
                var errors = new List<Exception> { publicationError };
                foreach (Change change in applied.AsEnumerable().Reverse())
                {
                    try
                    {
                        if (File.Exists(change.Path)) { File.Delete(change.Path); }
                        if (change.Backup is not null) { File.Move(change.Backup, change.Path); }
                    }
                    catch (Exception rollbackError) { errors.Add(rollbackError); }
                }
                if (errors.Count > 1)
                {
                    preserveBackups = true;
                    throw new AggregateException("Shader output publication and rollback failed; backups remain in " + staging, errors);
                }
                throw;
            }
        }
        finally
        {
            if (!preserveBackups)
            {
                foreach (string file in Directory.EnumerateFiles(staging)) { File.Delete(file); }
                Directory.Delete(staging);
            }
        }
    }

    private static async Task AddChangeAsync(string path, byte[] bytes, List<Change> changes, CancellationToken token)
    {
        if (Directory.Exists(path)) { throw new IOException("A generated file path is already a directory: " + path); }
        if (File.Exists(path) && (await File.ReadAllBytesAsync(path, token)).AsSpan().SequenceEqual(bytes)) { return; }
        changes.Add(new(path, bytes));
    }

    private static byte[] InputList(string directory, IEnumerable<string> files, string suffix)
    {
        string[] paths = files.Where(name => name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            .Select(name => Resolve(directory, name)).ToArray();
        return Encoding.UTF8.GetBytes(paths.Length == 0 ? string.Empty : string.Join('\n', paths) + "\n");
    }

    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || Path.IsPathRooted(name))
        { throw new ArgumentException("Generated output names must be relative request/file paths.", nameof(name)); }
        string[] segments = name.Replace('\\', '/').Split('/');
        if (segments.Length != 2 || segments.Any(segment => segment.Length == 0 || segment is "." or ".." ||
            segment.EndsWith(' ') || segment.EndsWith('.') || segment.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0 ||
            segment.Any(char.IsControl) || IsReserved(segment)))
        { throw new ArgumentException("Generated output names require a request identifier and a plain, non-reserved filename.", nameof(name)); }
        return string.Join('/', segments);
    }

    private static bool IsReserved(string name) => name.Equals(Inventory, StringComparison.OrdinalIgnoreCase)
        || name.Equals(Sources, StringComparison.OrdinalIgnoreCase) || name.Equals(Resources, StringComparison.OrdinalIgnoreCase);

    private static string Resolve(string directory, string relative)
    {
        string path = Path.GetFullPath(Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar)));
        string prefix = Path.TrimEndingDirectorySeparator(directory) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        { throw new ArgumentException("Generated output paths must stay within the output directory.", nameof(relative)); }
        RejectLink(Path.GetDirectoryName(path)!);
        RejectLink(path);
        return path;
    }

    private static void RejectLink(string path)
    {
        if (Path.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        { throw new IOException("Generated outputs cannot traverse a symbolic link or reparse point: " + path); }
    }

    private sealed class Change(string path, byte[]? bytes)
    {
        internal string Path { get; } = path;
        internal byte[]? Bytes { get; } = bytes;
        internal string? Staged { get; set; }
        internal string? Backup { get; set; }
    }
}
