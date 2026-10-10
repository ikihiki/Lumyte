namespace Lumyte.Animation;

internal readonly record struct EventQuery(long From, long To, bool IncludeFrom, bool IncludeTo, Int128 Origin, int Direction)
{
    internal bool Contains(long time) => (time > From || (IncludeFrom && time == From)) && (time < To || (IncludeTo && time == To));

    internal EventQuery Offset(long offset) => new(From - offset, To - offset, IncludeFrom, IncludeTo, checked(Origin + (Direction * offset)), Direction);

    internal bool Clamp(long length, out EventQuery bounded)
    {
        long from = Math.Max(0, From);
        long to = Math.Min(length, To);
        bounded = new EventQuery(from, to, From < 0 || IncludeFrom, To > length || IncludeTo, Origin, Direction);
        return from < to || (from == to && bounded.IncludeFrom && bounded.IncludeTo);
    }
}
