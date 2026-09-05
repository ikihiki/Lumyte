namespace Lumyte.Interaction;

public abstract class GestureRecognizer
{
    public abstract GestureRecognition? Process(in GestureInput input);

    public virtual void Reset()
    {
    }
}
