namespace Lumyte.Diagnostics;

/// <summary>Writes named scalar values without constructing transport values or dictionaries.</summary>
public interface IDiagnosticValueWriter
{
    /// <summary>Writes a boolean.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="value">The boolean value.</param>
    void Write(string name, bool value);

    /// <summary>Writes an integer.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="value">The integer value.</param>
    void Write(string name, long value);

    /// <summary>Writes a floating-point number.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="value">The floating-point value.</param>
    void Write(string name, double value);

    /// <summary>Writes a string.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="value">The string value.</param>
    void Write(string name, string value);
}
