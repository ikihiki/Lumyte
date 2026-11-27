using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning;

internal sealed class PreparedShaderDataUpload<T>(IGraphicsShaderDataBuffer<T> staging, IReadOnlyList<IArgumentPiece>[] captured) : IPreparedTransfer
    where T : struct, IShaderArguments
{
    public ValueTask MapAsync() => staging.MapAsync();

    public void Complete()
    {
        var values = new PreparedArguments<T>[captured.Length];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = new(captured[i]);
        }

        staging.CopyFrom<PreparedArguments<T>>(values);
        staging.Unmap();
    }

    public void Dispose()
    {
        if (staging.IsMapped)
        {
            staging.Unmap();
        }
    }
}
