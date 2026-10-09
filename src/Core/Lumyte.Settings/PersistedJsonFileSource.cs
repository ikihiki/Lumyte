namespace Lumyte.Settings;

/// <summary>Loads a shared JSON document synchronously when configuration is loaded.</summary>
public sealed class PersistedJsonFileSource : PersistedSettingsSource
{
    private readonly JsonFileSettingsStore _store;

    /// <summary>Initializes a new instance of the <see cref="PersistedJsonFileSource"/> class.</summary>
    /// <param name="path">The absolute file path.</param>
    public PersistedJsonFileSource(string path)
        : this(new JsonFileSettingsStore(path))
    {
    }

    private PersistedJsonFileSource(JsonFileSettingsStore store)
        : base(store)
    {
        _store = store;
    }

    private protected override byte[]? ReadSynchronously() => _store.Read();
}
