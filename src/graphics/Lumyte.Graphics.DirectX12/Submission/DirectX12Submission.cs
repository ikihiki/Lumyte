using Lumyte.Graphics.Native;

namespace Lumyte.Graphics.DirectX12;

// The queue prepares and owns command memory before entering this native handoff sequence.
// The constrained call contract lets tests inject failures without creating a GPU device.
internal static class DirectX12Submission
{
    internal static void Execute<TCalls>(NativeGpuTimelinePoint completion, int waitCount, TCalls calls)
        where TCalls : IDirectX12SubmissionCalls
    {
        try
        {
            for (int index = 0; index < waitCount; index++) { calls.Wait(index); }
            calls.ExecuteCommands();
            calls.SignalInternal();
            calls.SignalCaller();
        }
        catch (Exception error)
        {
            calls.Fault();
            throw new NativeGpuSubmissionException(completion, error);
        }
    }
}

internal interface IDirectX12SubmissionCalls
{
    void Wait(int index);
    void ExecuteCommands();
    void SignalInternal();
    void SignalCaller();
    void Fault();
}
