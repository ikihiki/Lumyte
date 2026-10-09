using System.Text.Json.Nodes;

namespace Lumyte.Settings;

/// <summary>Internal module operations used by the shared coordinator.</summary>
internal interface ISettingsSlot
{
    /// <summary>Gets the stable module ID.</summary>
    string SectionId { get; }

    /// <summary>Initializes the module once.</summary>
    void EnsureInitialized();

    /// <summary>Builds validated defaults for a whole-document reset.</summary>
    /// <returns>The value and serialized section.</returns>
    (object Value, JsonObject Section) PrepareDefaults();

    /// <summary>Publishes a committed candidate while holding the document lock.</summary>
    /// <param name="value">The committed candidate.</param>
    /// <param name="recovering">Whether to remove section protection.</param>
    void Commit(object value, bool recovering);
}
