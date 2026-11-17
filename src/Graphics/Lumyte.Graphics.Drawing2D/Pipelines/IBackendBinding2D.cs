namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Graph-local frozen binding metadata.</summary>
internal interface IBackendBinding2D
{
    /// <summary>Gets the compiled SharedIdentity.</summary>
    object? SharedIdentity { get; }

    /// <summary>Gets the compiled SharedName.</summary>
    string? SharedName { get; }

    /// <summary>Gets the frozen candidate order for an explicitly bound FirstSupported backend.</summary>
    IReadOnlyList<Type> ChoiceBackends { get; }

    /// <summary>Gets the compiled ConcreteType.</summary>
    Type ConcreteType { get; }
}
