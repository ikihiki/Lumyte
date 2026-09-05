using System.Drawing;

using Lumyte.Platform;
using Lumyte.Platform.Windows;

using Microsoft.Extensions.Hosting;

namespace Lumyte.DevTools.Host;

public interface IDemoWindowRunner { void Run(DemoInputDomain input, CancellationToken stoppingToken, Action windowClosed, Action started); }

public sealed class WindowsDemoWindowRunner : IDemoWindowRunner
{
    public void Run(DemoInputDomain input, CancellationToken stoppingToken, Action windowClosed, Action started)
    {
        if (!OperatingSystem.IsWindows())
        { started(); stoppingToken.WaitHandle.WaitOne(); return; }
        using var platform = new WindowsPlatform();
        using WindowsWindow window = platform.CreateWindow(new WindowOptions { Title = "Lumyte DevTools Input Demo", ClientSize = new Size(800, 500), IsVisible = true });
        WindowsWindowInput windowInput = platform.Input.GetWindow(window);
        using IDisposable attachment = input.AttachWindow(window, windowInput, "demo-window");
        window.CloseRequested += (_, _) => window.Close();
        started();
        while (!stoppingToken.IsCancellationRequested && platform.PumpEvents())
        {
            Thread.Sleep(8);
        }

        if (!stoppingToken.IsCancellationRequested)
        {
            windowClosed();
        }
    }
}

public sealed class DemoWindowHostedService(DemoInputDomain input, IDemoWindowRunner runner, IHostApplicationLifetime lifetime) : IHostedService
{
    private readonly CancellationTokenSource stopping = new(); private Thread? thread;
    public Task StartAsync(CancellationToken cancellationToken) { var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); thread = new Thread(() => { try { runner.Run(input, stopping.Token, lifetime.StopApplication, () => started.TrySetResult()); } catch (Exception exception) { started.TrySetException(exception); lifetime.StopApplication(); } }) { IsBackground = true, Name = "Lumyte Demo Window" }; if (OperatingSystem.IsWindows()) { thread.SetApartmentState(ApartmentState.STA); } thread.Start(); return started.Task.WaitAsync(cancellationToken); }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        stopping.Cancel();
        if (thread is not null)
        {
            await Task.Run(thread.Join, cancellationToken);
        }
    }
}
