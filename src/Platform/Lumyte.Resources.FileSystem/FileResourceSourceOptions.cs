namespace Lumyte.Resources.FileSystem;

/// <summary>Configures direct access to files in an application-owned directory tree.</summary>
public sealed record FileResourceSourceOptions
{
    /// <summary>Gets the directory containing resource files. Symbolic links in its ancestry or contents are rejected.</summary>
    public required string RootPath { get; init; }
}
