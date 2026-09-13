using Lumyte.Graphics.Native;
using Silk.NET.Vulkan;

namespace Lumyte.Graphics.Vulkan;

// Own possibly submitted command memory before creating diagnostic exceptions.
internal static class VulkanSubmission
{
    internal static void Execute<TCalls>(NativeGpuTimelinePoint completion, TCalls calls)
        where TCalls : IVulkanSubmissionCalls
    {
        Result result;
        try { result = calls.Submit(); }
        catch (Exception error)
        {
            calls.Retain(false);
            throw new NativeGpuSubmissionException(completion, error);
        }

        // Only these native failures guarantee that submitted resources and semaphores
        // are unaffected. In particular, DEVICE_LOST does not prove non-submission.
        if (result is Result.ErrorOutOfHostMemory or Result.ErrorOutOfDeviceMemory)
        {
            calls.CheckResult(result);
            return;
        }

        calls.Retain(result == Result.Success);
        try { calls.CheckResult(result); }
        catch (Exception error) { throw new NativeGpuSubmissionException(completion, error); }
    }
}

internal interface IVulkanSubmissionCalls
{
    Result Submit();
    void Retain(bool completionSignalKnown);
    void CheckResult(Result result);
}
