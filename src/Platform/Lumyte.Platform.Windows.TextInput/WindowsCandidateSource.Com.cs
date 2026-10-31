namespace Lumyte.Platform.Windows.TextInput;

internal sealed unsafe partial class WindowsCandidateSource
{
    HRESULT ITfUIElementSink.Interface.BeginUIElement(uint dwUIElementId, BOOL* pbShow) => (HRESULT)BeginUIElement(dwUIElementId, (int*)pbShow);

    HRESULT ITfUIElementSink.Interface.UpdateUIElement(uint dwUIElementId) => (HRESULT)UpdateUIElement(dwUIElementId);

    HRESULT ITfUIElementSink.Interface.EndUIElement(uint dwUIElementId) => (HRESULT)EndUIElement(dwUIElementId);
}
