using System.Reflection;

namespace Lumyte.Resources.Embedded;

/// <summary>Opens immutable DLL-embedded data without copying its contents into a managed buffer.</summary>
/// <remarks>The source borrows the assembly and does not load, replace or unload DLLs. Linked external resources are not supported.</remarks>
public sealed class EmbeddedResourceSource : IResourceSource
{
    private readonly Assembly _assembly;
    private readonly Dictionary<ResourceId, string> _names;
    private readonly string _moduleRevision;

    /// <summary>Initializes a new instance of the <see cref="EmbeddedResourceSource"/> class.</summary>
    /// <param name="options">The assembly and optional logical identifier aliases.</param>
    public EmbeddedResourceSource(EmbeddedResourceSourceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.Assembly);
        ArgumentNullException.ThrowIfNull(options.ResourceNames);
        _assembly = options.Assembly;
        _names = new(options.ResourceNames);
        foreach ((ResourceId id, string name) in _names)
        {
            if (id.Value is null)
            {
                throw new ArgumentException("Supply initialized resource identifiers for aliases.", nameof(options));
            }

            ArgumentException.ThrowIfNullOrEmpty(name);
        }

        _moduleRevision = _assembly.ManifestModule.ModuleVersionId.ToString("N");
    }

    /// <inheritdoc />
    public ValueTask<ResourceSourceSnapshot> OpenSnapshotAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string name = Resolve(id);
        if (!IsEmbedded(name))
        {
            throw new FileNotFoundException($"Embedded resource '{name}' was not found in '{_assembly.FullName}'.", name);
        }

        using Stream input = OpenInput(_assembly, name);
        if (!input.CanSeek)
        {
            throw new NotSupportedException("Embedded resource streams must support seeking.");
        }

        ResourceSourceSnapshot snapshot = new EmbeddedResourceSnapshot(_assembly, id, name, Revision(name), input.Length);
        return ValueTask.FromResult(snapshot);
    }

    /// <inheritdoc />
    public ValueTask<string?> GetRevisionAsync(ResourceId id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string name = Resolve(id);
        return ValueTask.FromResult(IsEmbedded(name) ? Revision(name) : null);
    }

    internal static Stream OpenInput(Assembly assembly, string name)
        => assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException($"Embedded resource '{name}' was not found.", name);

    private bool IsEmbedded(string name)
    {
        ManifestResourceInfo? information = _assembly.GetManifestResourceInfo(name);
        if (information is null)
        {
            return false;
        }

        if ((information.ResourceLocation & ResourceLocation.Embedded) == 0 || information.ReferencedAssembly is not null)
        {
            throw new NotSupportedException("Only resources embedded directly in the configured assembly are supported.");
        }

        return true;
    }

    private string Resolve(ResourceId id)
    {
        if (id.Value is null)
        {
            throw new ArgumentException("Supply an initialized resource identifier.", nameof(id));
        }

        return _names.GetValueOrDefault(id, id.Value);
    }

    private string Revision(string name) => $"{_moduleRevision}:{name}";
}
