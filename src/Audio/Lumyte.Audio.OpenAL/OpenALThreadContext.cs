using Silk.NET.OpenAL;

namespace Lumyte.Audio.OpenAL;

// Silk.NET 2.23.0 does not expose ALC_EXT_thread_local_context. Resolve only
// these two extension functions through its existing OpenAL loader.
internal sealed unsafe class OpenALThreadContext
{
    private readonly delegate* unmanaged[Cdecl]<Context*, byte> _setCurrent;
    private readonly delegate* unmanaged[Cdecl]<Context*> _getCurrent;

    internal OpenALThreadContext(ALContext api, Device* device)
    {
        if (!api.IsExtensionPresent(device, "ALC_EXT_thread_local_context"))
        {
            throw new NotSupportedException("OpenAL requires ALC_EXT_thread_local_context; install OpenAL Soft.");
        }

        _setCurrent = (delegate* unmanaged[Cdecl]<Context*, byte>)api.GetProcAddress(device, "alcSetThreadContext");
        _getCurrent = (delegate* unmanaged[Cdecl]<Context*>)api.GetProcAddress(device, "alcGetThreadContext");
        if (_setCurrent == null || _getCurrent == null)
        {
            throw new NotSupportedException("OpenAL thread-local context entry points are unavailable.");
        }
    }

    internal void SetCurrent(Context* context)
    {
        if (_setCurrent(context) == 0)
        {
            throw new InvalidOperationException("OpenAL thread-local context activation failed.");
        }
    }

    internal void ClearIfCurrent(Context* context)
    {
        if (_getCurrent() == context)
        {
            SetCurrent(null);
        }
    }
}
