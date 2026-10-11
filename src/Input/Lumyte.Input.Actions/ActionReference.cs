namespace Lumyte.Input.Actions;

/// <summary>References an action definition without repeating its persisted identifier.</summary>
public readonly struct ActionReference
{
    private readonly string? _id;
    private readonly Compose.Definitions.Action? _definition;

    /// <summary>Initializes a new instance of the <see cref="ActionReference"/> struct.</summary>
    /// <param name="definition">The action construction node.</param>
    public ActionReference(Compose.Definitions.Action definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
        _id = null;
    }

    /// <summary>Initializes a new instance of the <see cref="ActionReference"/> struct.</summary>
    /// <param name="id">The explicit identifier for compatibility with stored definitions.</param>
    public ActionReference(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _id = id;
        _definition = null;
    }

    /// <summary>Gets the referenced action's persisted identifier.</summary>
    public string Id => _definition?.Id ?? _id ?? throw new InvalidOperationException("The action reference is uninitialized.");

    internal Compose.Definitions.Action? Definition => _definition;

    /// <summary>References an action construction node.</summary>
    /// <param name="definition">The action node.</param>
    /// <returns>The definition reference.</returns>
    public static implicit operator ActionReference(Compose.Definitions.Action definition) => new(definition);

    /// <summary>References an existing stored identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The identifier reference.</returns>
    public static implicit operator ActionReference(string id) => new(id);
}
