namespace Lumyte.UI.Collections;

/// <summary>Immutable named column settings with application-supplied ordering.</summary>
/// <typeparam name="T">The row type.</typeparam>
public sealed record DataGridColumn<T>
    where T : notnull
{
    /// <summary>Initializes a new instance of the <see cref="DataGridColumn{T}"/> class.</summary>
    /// <param name="key">Nonempty ordinal column identity.</param>
    /// <param name="name">Explicit accessible name.</param>
    /// <param name="width">Finite initial width within the bounds.</param>
    /// <param name="minimumWidth">Finite positive minimum.</param>
    /// <param name="maximumWidth">Finite maximum at least the minimum.</param>
    /// <param name="isVisible">Whether this column participates in the table.</param>
    /// <param name="comparer">Explicit ordering, or null to disable sorting.</param>
    public DataGridColumn(string key, string name, float width = 120, float minimumWidth = 24, float maximumWidth = 4096, IComparer<T>? comparer = null, bool isVisible = true)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (key.Length > 256 || name.Length > 256)
        {
            throw new ArgumentException("Column identities and names are bounded to 256 characters.", nameof(key));
        }

        if (!float.IsFinite(width) || !float.IsFinite(minimumWidth) || !float.IsFinite(maximumWidth)
            || minimumWidth <= 0 || maximumWidth < minimumWidth || width < minimumWidth || width > maximumWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        Key = key;
        Name = name;
        Width = width;
        MinimumWidth = minimumWidth;
        MaximumWidth = maximumWidth;
        Comparer = comparer;
        IsVisible = isVisible;
    }

    /// <summary>Gets the ordinal identity.</summary>
    public string Key { get; }

    /// <summary>Gets the explicit accessible name.</summary>
    public string Name { get; }

    /// <summary>Gets the declared width.</summary>
    public float Width { get; }

    /// <summary>Gets the minimum width.</summary>
    public float MinimumWidth { get; }

    /// <summary>Gets the maximum width.</summary>
    public float MaximumWidth { get; }

    /// <summary>Gets a value indicating whether the column participates in the table.</summary>
    public bool IsVisible { get; }

    /// <summary>Gets explicit ordering, or null for an unsortable column.</summary>
    public IComparer<T>? Comparer { get; }
}
