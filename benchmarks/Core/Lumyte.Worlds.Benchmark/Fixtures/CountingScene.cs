namespace Lumyte.Worlds.Benchmark;

internal sealed class CountingScene : Scene
{
    internal int Fixed { get; private set; }

    internal int Updated { get; private set; }

    internal int Late { get; private set; }

    internal int Drawn { get; private set; }

    internal int Input { get; private set; }

    internal bool Consume { get; set; }

    public override void FixedUpdate(TimeSpan step) => Fixed = unchecked(Fixed + 1);

    public override void Update(TimeSpan elapsed) => Updated = unchecked(Updated + 1);

    public override void LateUpdate(TimeSpan elapsed) => Late = unchecked(Late + 1);

    public override void Draw(DrawContext context) => Drawn = unchecked(Drawn + 1);

    public override bool HandleInput()
    {
        Input = unchecked(Input + 1);
        return Consume;
    }
}
