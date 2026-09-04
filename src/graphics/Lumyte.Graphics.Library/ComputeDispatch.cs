namespace Lumyte.Graphics.Library;

public readonly record struct ComputeDispatch(uint GroupCountX, uint GroupCountY = 1, uint GroupCountZ = 1)
{
    public ComputeDispatch Validate()
    {
        if (GroupCountX == 0 || GroupCountY == 0 || GroupCountZ == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(GroupCountX));
        }
        return this;
    }
}
