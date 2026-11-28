namespace Lumyte.Graphics.Planning;

/// <summary>Captures a typed symbolic shader reference in its declaring operation.</summary>
internal interface IPlanReference
{
    /// <summary>Declares capture.</summary>
    /// <param name = "pass">The pass.</param>
    /// <returns>The declared result.</returns>
    IArgumentPiece Capture(PlanPass pass);

    /// <summary>Captures a same-frame symbolic reference for a declared shader-data payload.</summary>
    /// <param name="pass">The upload declaration scope.</param>
    /// <returns>The typed immutable reference recipe.</returns>
    IArgumentPiece CaptureForData(PlanPass pass);
}
