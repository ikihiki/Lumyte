namespace Lumyte.Graphics.Drawing2D.Pipelines;

/// <summary>Describes the exact frozen candidate order without evaluating admissions.</summary>
internal interface ICommandChoice2D
{
    /// <summary>Gets the candidate backend types in first-supported order.</summary>
    IReadOnlyList<Type> CandidateTypes { get; }
}
