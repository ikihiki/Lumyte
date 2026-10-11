using System.Runtime.Versioning;
using Lumyte.Platform.Abstractions;
using Lumyte.Windowing.Abstractions;

namespace Lumyte.Platform.Windows.Sample;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            Console.WriteLine("This sample requires Windows 10 version 1809 or later.");
            return;
        }

        Run();
    }

    [SupportedOSPlatform("windows10.0.17763")]
    private static void Run()
    {
        using var platform = new WindowsPlatform();
        using IWindow window = platform.CreateWindow(new WindowDescriptor { Title = "Lumyte TSF — type here" });
        using ITextInputService text = platform.CreateTextInputService();
        using ITextInputSession session = text.CreateSession(window, new Document());
        window.Show();
        platform.PumpEvents();
        session.Focus();
        while (window.State.Lifetime == WindowLifetime.Open && !platform.IsQuitRequested)
        {
            platform.PumpEvents();
            Thread.Sleep(10);
        }

        if (window.State.Lifetime == WindowLifetime.Open)
        {
            window.Close();
        }

        // The text session retains the HWND. Release it to complete native closure.
        session.Dispose();
        platform.PumpEvents();
    }

    private sealed class Document : ITextInputClient
    {
        public string Text { get; private set; } = string.Empty;

        public TextSelection Selection { get; private set; }

        public bool IsReadOnly => false;

        public void Replace(int start, int length, string text)
        {
            Text = Text.Remove(start, length).Insert(start, text);
            Selection = new TextSelection(start + text.Length, 0);
            Console.WriteLine(Text);
        }

        public void SetSelection(TextSelection selection) => Selection = selection;

        public TextInputBounds GetBounds(int start, int length) => new(new WindowRect(20 + (start * 16), 20, Math.Max(1, length * 16), 24), false);
    }
}
