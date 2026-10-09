namespace Lumyte.Settings;

/// <summary>An editable, detached candidate owned by one settings service.</summary>
/// <typeparam name="T">The settings model.</typeparam>
public sealed class SettingsEdit<T>
    where T : class, new()
{
    internal SettingsEdit(object owner, long revision, T value)
    {
        Owner = owner;
        BaseRevision = revision;
        Value = value;
    }

    /// <summary>Gets the revision captured when editing began.</summary>
    public long BaseRevision { get; }

    /// <summary>Gets the editable value.</summary>
    public T Value { get; }

    internal object Owner { get; }
}
