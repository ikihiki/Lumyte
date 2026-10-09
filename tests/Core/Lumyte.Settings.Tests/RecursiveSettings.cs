namespace Lumyte.Settings.Tests;

/// <summary>Exercises the document depth limit independently from the model serializer's limit.</summary>
public sealed class RecursiveSettings
{
    /// <summary>Gets or sets the next nested value.</summary>
    public RecursiveSettings? Child { get; set; }
}
