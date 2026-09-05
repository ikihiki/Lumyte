namespace Lumyte.Animation;

public sealed class StepsCurve : ICurve
{
    public StepsCurve(int count, StepPosition position = StepPosition.JumpEnd)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "The step count must be positive.");
        }

        if (position == StepPosition.JumpNone && count == 1)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "JumpNone requires at least two steps.");
        }

        Count = count;
        Position = position;
    }

    public int Count { get; }

    public StepPosition Position { get; }

    public float Transform(float progress)
    {
        var clamped = Math.Clamp(progress, 0f, 1f);
        return Position switch
        {
            StepPosition.JumpStart => MathF.Min(MathF.Floor(clamped * Count) + 1f, Count) / Count,
            StepPosition.JumpEnd => MathF.Floor(clamped * Count) / Count,
            StepPosition.JumpBoth => MathF.Floor((clamped * Count) + 1f) / (Count + 1f),
            StepPosition.JumpNone => MathF.Min(MathF.Floor(clamped * Count), Count - 1f) / (Count - 1f),
            _ => throw new InvalidOperationException("The step position is not supported."),
        };
    }
}
