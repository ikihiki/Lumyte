using Xilium.CefGlue;

// A selection probe, not a supported WebView launcher. Never use the Common loader:
// that loader unconditionally changes NoSandbox to true on Linux.
if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("This sandbox acceptance probe currently targets Linux only.");
    return 2;
}

Console.WriteLine($"CefGlue ABI: {CefRuntime.ChromeVersion}; sandbox requested: true");
CefRuntime.Load();
var main = new CefMainArgs(new[] { Environment.ProcessPath! }.Concat(args).ToArray());
int result = CefRuntime.ExecuteProcess(main, null, IntPtr.Zero);
if (result >= 0)
{
    return result;
}

var settings = new CefSettings
{
    NoSandbox = false,
    WindowlessRenderingEnabled = true,
    MultiThreadedMessageLoop = false,
    BrowserSubprocessPath = Environment.ProcessPath,
    LogSeverity = CefLogSeverity.Disable,
};
CefRuntime.Initialize(main, settings, null, IntPtr.Zero);
Console.WriteLine("CEF initialized with sandbox requested; OSR browser creation remains a separate acceptance gate.");
CefRuntime.Shutdown();
return 0;
