using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lumyte.Settings;

internal sealed class SettingsDocument : ISettingsDocument, IDisposable
{
    private readonly PersistedSettingsSource _source;
    private readonly IServiceProvider _services;
    private readonly PersistedOptionsExtensions.Registration[] _registrations;
    private JsonObject _document;
    private bool _protected;

    public SettingsDocument(PersistedSettingsSource source, IServiceProvider services, IEnumerable<PersistedOptionsExtensions.Registration> registrations)
    {
        if (!source.IsLoaded || !source.IsRegistered)
        {
            throw new InvalidOperationException("Register and load the source in configuration before resolving settings.");
        }

        _source = source;
        _services = services;
        _registrations = registrations.ToArray();
        _document = source.CopyDocument();
        _protected = source.Result.Status is not (SettingsLoadStatus.Loaded or SettingsLoadStatus.Defaults);
    }

    internal object Sync { get; } = new();

    internal SemaphoreSlim Writes { get; } = new(1, 1);

    internal SettingsLoadResult LoadResult => _source.Result;

    internal bool IsProtected => _protected;

    public void ValidateRegisteredSettings()
    {
        foreach (PersistedOptionsExtensions.Registration registration in _registrations)
        {
            registration.Resolve(_services).EnsureInitialized();
        }
    }

    public async Task<SettingsDocumentSaveResult> ResetAsync(CancellationToken cancellationToken = default)
    {
        ValidateRegisteredSettings();
        (ISettingsSlot Slot, (object Value, JsonObject Section) Prepared)[] defaults;
        try
        {
            defaults = _registrations.Select(registration => registration.Resolve(_services)).Select(slot => (Slot: slot, Prepared: slot.PrepareDefaults())).ToArray();
        }
        catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException)
        {
            return new(SettingsSaveStatus.ValidationFailed, [error.Message]);
        }

        await Writes.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JsonObject replacement = PersistedSettingsSource.EmptyDocument();
            var sections = (JsonObject)replacement["sections"]!;
            foreach ((ISettingsSlot Slot, (object Value, JsonObject Section) Prepared) entry in defaults)
            {
                sections[entry.Slot.SectionId] = entry.Prepared.Section;
            }

            try
            {
                await _source.Store.WriteAtomicallyAsync(Serialize(replacement), cancellationToken).ConfigureAwait(false);
            }
            catch (IOException error)
            {
                return new(SettingsSaveStatus.StorageFailure, [error.Message]);
            }
            catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException)
            {
                return new(SettingsSaveStatus.ValidationFailed, [error.Message]);
            }

            lock (Sync)
            {
                _document = replacement;
                _protected = false;
                _source.Publish(replacement);
                foreach ((ISettingsSlot Slot, (object Value, JsonObject Section) Prepared) entry in defaults)
                {
                    entry.Slot.Commit(entry.Prepared.Value, recovering: true);
                }
            }

            return new(SettingsSaveStatus.Saved, []);
        }
        finally
        {
            Writes.Release();
        }
    }

    public void Dispose() => Writes.Dispose();

    internal JsonNode? ReadSection(string sectionId) => _document["sections"]![sectionId]?.DeepClone();

    internal bool ContainsSection(string sectionId) => ((JsonObject)_document["sections"]!).ContainsKey(sectionId);

    internal JsonObject ReplaceSection(string sectionId, JsonObject section)
    {
        var next = (JsonObject)_document.DeepClone();
        next["sections"]![sectionId] = section;
        return next;
    }

    internal async Task StoreAsync(JsonObject replacement, CancellationToken cancellationToken) => await _source.Store.WriteAtomicallyAsync(Serialize(replacement), cancellationToken).ConfigureAwait(false);

    internal void Publish(JsonObject replacement)
    {
        _document = replacement;
        _source.Publish(replacement);
    }

    private static byte[] Serialize(JsonObject document) => SettingsJson.SerializeDocument(document);
}
