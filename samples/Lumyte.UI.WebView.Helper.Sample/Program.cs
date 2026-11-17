using System.Text;
using Lumyte.UI.Application.WebView.Cef;

if (args.Contains("--detached-child", StringComparer.Ordinal))
{
    NativeMethods.SetSid();
    await Task.Delay(Timeout.Infinite);
    return;
}

if (args.Contains("--selfcheck", StringComparer.Ordinal))
{
    using var stream = new MemoryStream();
    await CefHelperProtocol.WriteAsync(stream, new(103, 7, Width: 1, Height: 1, Body: [20, 40, 80, 255]), CancellationToken.None);
    stream.Position = 0;
    CefHelperPacket roundtrip = await CefHelperProtocol.ReadAsync(stream, CancellationToken.None);
    if (roundtrip.Generation != 7 || roundtrip.Body!.Length != 4)
    {
        throw new InvalidDataException("helper-selfcheck");
    }

    Console.WriteLine("Bounded helper protocol selfcheck passed (no CEF).");
    return;
}

// Engine-free IPC fault injection. Never selected by a normal application implicitly.
string mode = Path.GetFileName(Environment.ProcessPath)!.Split('.').First();
if (mode.EndsWith("argumentproof", StringComparison.Ordinal))
{
    File.AppendAllText(Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "launch-args.txt"), string.Join(" ", args) + "\n");
    Environment.Exit(134);
}

Stream input = Console.OpenStandardInput();
Stream output = Console.OpenStandardOutput();
CefHelperPacket start = await CefHelperProtocol.ReadAsync(input, CancellationToken.None);
if (mode.Contains("orphan-", StringComparison.Ordinal))
{
    string marker = Path.Combine(Path.GetDirectoryName(Environment.ProcessPath)!, "child.txt");
    System.Diagnostics.Process child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!)
    {
        ArgumentList = { "--detached-child" },
        UseShellExecute = false,
    })!;
    string home = Environment.GetEnvironmentVariable("HOME")!;
    int permissions = OperatingSystem.IsLinux() ? (int)File.GetUnixFileMode(home) : 0;
    File.WriteAllText(marker, child.Id + "\n" + home + "\n" + Convert.ToString(permissions, 8));
    Environment.Exit(mode.EndsWith("134", StringComparison.Ordinal) ? 134 : 0);
}

if (mode.EndsWith("slowready", StringComparison.Ordinal))
{
    await Task.Delay(500);
}

if (mode.EndsWith("sigabrt", StringComparison.Ordinal))
{
    NativeMethods.DisableCoreDump(4, default);
    NativeMethods.Raise(6);
    throw new InvalidOperationException("SIGABRT did not terminate fixture.");
}

if (mode.EndsWith("crash", StringComparison.Ordinal))
{
    Environment.Exit(134);
}

if (mode.EndsWith("-hang", StringComparison.Ordinal))
{
    await Task.Delay(Timeout.Infinite);
}

if (mode.EndsWith("malformed", StringComparison.Ordinal))
{
    await output.WriteAsync(new byte[44]);
    return;
}

await CefHelperProtocol.WriteAsync(output, new(101), CancellationToken.None);
if (mode.EndsWith("navigationhang", StringComparison.Ordinal))
{
    await Task.Delay(Timeout.Infinite);
}

if (mode.EndsWith("sequence-rewind", StringComparison.Ordinal))
{
    await CefHelperProtocol.WriteAsync(output, new(102, Sequence: 10, Body: start.Body), CancellationToken.None);
    await CefHelperProtocol.WriteAsync(output, new(102, Sequence: 9, Body: start.Body), CancellationToken.None);
    return;
}

if (mode.EndsWith("backpressure", StringComparison.Ordinal))
{
    await Task.Delay(Timeout.Infinite);
}

await CefHelperProtocol.WriteAsync(output, new(102, Body: start.Body), CancellationToken.None);
while (true)
{
    CefHelperPacket packet;
    try
    {
        packet = await CefHelperProtocol.ReadAsync(input, CancellationToken.None);
    }
    catch (EndOfStreamException)
    {
        return;
    }

    if (packet.Kind == 4)
    {
        return;
    }

    if (packet.Kind == 3)
    {
        byte[] pixels = new byte[checked(packet.Width * packet.Height * 4)];
        pixels.AsSpan().Fill(127);
        await CefHelperProtocol.WriteAsync(output, new(103, packet.Generation - 1, Width: packet.Width, Height: packet.Height, Body: pixels), CancellationToken.None);
        await CefHelperProtocol.WriteAsync(output, new(103, packet.Generation, Width: packet.Width, Height: packet.Height, Body: pixels), CancellationToken.None);
    }
    else if (packet.Kind is 2 or 5 or 6 or 7 or 8)
    {
        await CefHelperProtocol.WriteAsync(output, new(102, Sequence: packet.Sequence, Body: packet.Body!.Length == 0 ? Encoding.UTF8.GetBytes("https://example.test/") : packet.Body), CancellationToken.None);
    }
}

internal static partial class NativeMethods
{
    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "setsid")]
    internal static partial int SetSid();

    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "raise")]
    internal static partial int Raise(int signal);

    [System.Runtime.InteropServices.LibraryImport("libc", EntryPoint = "setrlimit")]
    internal static partial int DisableCoreDump(int resource, in ResourceLimit limit);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    internal struct ResourceLimit
    {
        internal nuint Current;
        internal nuint Maximum;
    }
}
