using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Text.Benchmarks;

internal static class NativeCompletion
{
    internal static IGraphicsSubmission Submit(IGraphicDevice device, IGraphicsCommandBuffer commands)
    {
        try
        {
            return device.Queue.Submit([commands]);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Submission outcome unconfirmed; no native teardown/reuse: {error}");
            Environment.Exit(3);
            throw;
        }
    }

    internal static async Task WaitAsync(IGraphicsSubmission submission)
    {
        try
        {
            await submission.WaitAsync();
            if (submission.Status != SubmissionStatus.Completed)
            {
                throw new InvalidOperationException("GPU completion was not confirmed successful.");
            }
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"GPU completion unconfirmed; no native teardown/reuse: {error}");
            Environment.Exit(3);
            throw;
        }
    }
}
