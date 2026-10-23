namespace Lumyte.Execution;

/// <summary>Admission authority required by work.</summary>
public enum WorkAdmission
{
    /// <summary>Normal.</summary>
    Normal,

    /// <summary>Startup.</summary>
    Startup,

    /// <summary>Cleanup.</summary>
    Cleanup,
}
