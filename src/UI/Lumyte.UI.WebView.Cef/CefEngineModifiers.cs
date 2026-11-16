namespace Lumyte.UI.WebView.Cef;

[Flags]
internal enum CefEngineModifiers : uint
{
    None = 0,
    CapsLock = 1,
    Shift = 2,
    Control = 4,
    Alt = 8,
    LeftButton = 16,
    MiddleButton = 32,
    RightButton = 64,
    Command = 128,
    NumLock = 256,
    KeyPad = 512,
    Left = 1024,
    Right = 2048,
    AltGr = 4096,
    Repeat = 8192,
}
