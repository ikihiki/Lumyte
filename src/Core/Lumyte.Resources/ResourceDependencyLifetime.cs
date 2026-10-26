namespace Lumyte.Resources;

/// <summary>Determines how long a build retains a borrowed dependency or auxiliary resource.</summary>
public enum ResourceDependencyLifetime
{
    /// <summary>Retains the dependency until construction and its cleanup have completed.</summary>
    Build,

    /// <summary>Retains the dependency until the output has been successfully released.</summary>
    Resident,
}
