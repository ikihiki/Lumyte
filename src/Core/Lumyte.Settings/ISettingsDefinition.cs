using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace Lumyte.Settings;

/// <summary>Describes serialization, migration and cloning for a settings module.</summary>
/// <typeparam name="T">The settings model.</typeparam>
public interface ISettingsDefinition<T>
    where T : class, new()
{
    /// <summary>Gets the current section schema version.</summary>
    int SchemaVersion { get; }

    /// <summary>Gets serialization metadata, including source-generated metadata.</summary>
    JsonTypeInfo<T> JsonTypeInfo { get; }

    /// <summary>Copies the complete model without changing or rejecting invalid input values.</summary>
    /// <param name="value">The candidate to copy.</param>
    /// <returns>An independent copy, including all collections.</returns>
    T DeepClone(T value);

    /// <summary>Migrates a detached section without validating the typed model.</summary>
    /// <param name="values">The saved values.</param>
    /// <param name="sourceVersion">The saved schema version.</param>
    /// <returns>The migrated values.</returns>
    JsonObject Upgrade(JsonObject values, int sourceVersion);
}
