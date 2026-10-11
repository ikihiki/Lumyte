namespace Lumyte.Diagnostics.Settings;

/// <summary>Explicit scalar fields to expose from a settings model.</summary>
/// <typeparam name="T">The settings model.</typeparam>
public sealed class SettingsDiagnosticFields<T>
    where T : class, new()
{
    private readonly List<Field> _fields = [];
    private bool _sealed;

    /// <summary>Exposes a bool field; omit the setter for read-only access.</summary>
    /// <param name="id">The stable field ID.</param>
    /// <param name="get">Reads from a detached model.</param>
    /// <param name="set">Edits the detached candidate.</param>
    /// <returns>This definition.</returns>
    public SettingsDiagnosticFields<T> Boolean(string id, Func<T, bool> get, Action<T, bool>? set = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        return Add(new(new(id, DiagnosticValueKind.Boolean)) { GetBoolean = get, SetBoolean = set });
    }

    /// <summary>Exposes a long field; omit the setter for read-only access.</summary>
    /// <param name="id">The stable field ID.</param>
    /// <param name="get">Reads from a detached model.</param>
    /// <param name="set">Edits the detached candidate.</param>
    /// <returns>This definition.</returns>
    public SettingsDiagnosticFields<T> Int64(string id, Func<T, long> get, Action<T, long>? set = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        return Add(new(new(id, DiagnosticValueKind.Int64)) { GetInt64 = get, SetInt64 = set });
    }

    /// <summary>Exposes a double field; omit the setter for read-only access.</summary>
    /// <param name="id">The stable field ID.</param>
    /// <param name="get">Reads from a detached model.</param>
    /// <param name="set">Edits the detached candidate.</param>
    /// <returns>This definition.</returns>
    public SettingsDiagnosticFields<T> Double(string id, Func<T, double> get, Action<T, double>? set = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        return Add(new(new(id, DiagnosticValueKind.Double)) { GetDouble = get, SetDouble = set });
    }

    /// <summary>Exposes a string field; omit the setter for read-only access.</summary>
    /// <param name="id">The stable field ID.</param>
    /// <param name="get">Reads from a detached model.</param>
    /// <param name="set">Edits the detached candidate.</param>
    /// <param name="maxLength">The maximum published string length.</param>
    /// <returns>This definition.</returns>
    public SettingsDiagnosticFields<T> String(string id, Func<T, string> get, Action<T, string>? set = null, int maxLength = 4096)
    {
        ArgumentNullException.ThrowIfNull(get);
        return Add(new(new(id, DiagnosticValueKind.String, MaxLength: maxLength)) { GetString = get, SetString = set });
    }

    internal Field[] Seal()
    {
        if (_fields.Count == 0)
        {
            throw new InvalidOperationException("Expose at least one settings field.");
        }

        _sealed = true;
        return _fields.ToArray();
    }

    private SettingsDiagnosticFields<T> Add(Field field)
    {
        if (_sealed || _fields.Count >= 64 || string.IsNullOrWhiteSpace(field.Schema.Id) || field.Schema.Id == "load-status"
            || _fields.Any(item => item.Schema.Id == field.Schema.Id) || field.Schema.MaxLength is < 0 or > 4096)
        {
            throw new ArgumentException("Invalid, duplicate, reserved or sealed settings field.", nameof(field));
        }

        _fields.Add(field);
        return this;
    }

    internal sealed class Field(DiagnosticField schema)
    {
        public DiagnosticField Schema { get; } = schema;

        public Func<T, bool>? GetBoolean { get; init; }

        public Action<T, bool>? SetBoolean { get; init; }

        public Func<T, long>? GetInt64 { get; init; }

        public Action<T, long>? SetInt64 { get; init; }

        public Func<T, double>? GetDouble { get; init; }

        public Action<T, double>? SetDouble { get; init; }

        public Func<T, string>? GetString { get; init; }

        public Action<T, string>? SetString { get; init; }

        public bool Editable => SetBoolean != null || SetInt64 != null || SetDouble != null || SetString != null;

        public void Apply(T model, DiagnosticArguments arguments)
        {
            SetBoolean?.Invoke(model, arguments.GetBoolean(Schema.Id));
            SetInt64?.Invoke(model, arguments.GetInt64(Schema.Id));
            SetDouble?.Invoke(model, arguments.GetDouble(Schema.Id));
            SetString?.Invoke(model, arguments.GetString(Schema.Id));
        }

        public void Write<TWriter>(T model, ref TWriter writer)
            where TWriter : IDiagnosticValueWriter, allows ref struct
        {
            if (GetBoolean != null)
            {
                writer.Write(Schema.Id, GetBoolean(model));
            }
            else if (GetInt64 != null)
            {
                writer.Write(Schema.Id, GetInt64(model));
            }
            else if (GetDouble != null)
            {
                writer.Write(Schema.Id, GetDouble(model));
            }
            else
            {
                writer.Write(Schema.Id, GetString!(model));
            }
        }
    }
}
