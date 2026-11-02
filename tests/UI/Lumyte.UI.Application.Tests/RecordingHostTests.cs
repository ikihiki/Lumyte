using System.Numerics;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.Windowing;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Tests;

/// <summary>Checks the application submits cached recordings on successive real owner turns.</summary>
public sealed class RecordingHostTests
{
    /// <summary>Checks cache hits still traverse the real application driver submission boundary.</summary>
    /// <returns>The verification.</returns>
    [Fact]
    public async Task StandardHostSubmitsEveryCachedFrameAsync()
    {
        var driver = new Driver();
        UiApplicationBuilder builder = UiApplication.CreateBuilder().Configure(new() { FrameInterval = TimeSpan.FromMilliseconds(1) });
        builder.UseDriver(() => driver);
        builder.UseRoot(_ => Rectangle(fill: new Color2D(1, 0, 0, 1)));
        await using UiApplication app = builder.Build();
        Task running = app.RunAsync();
        try
        {
            await driver.Reused.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            await app.StopAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await running.WaitAsync(TimeSpan.FromSeconds(15));
        }

        Assert.True(driver.Draws >= 4);
        Assert.True(driver.Reuses >= 3);
    }

    private sealed class Driver : UiApplicationDriver
    {
        private DrawList2D? _previous;

        internal TaskCompletionSource Reused { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int Draws { get; private set; }

        internal int Reuses { get; private set; }

        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal override ValueTask InitializeAsync(UiApplication application, IServiceProvider services, bool textInput, CancellationToken cancellationToken) => ValueTask.CompletedTask;

        internal override ValueTask<UiDriverWindow> CreateWindowAsync(string key, string title, Vector2 size, UiDriverWindow? owner, bool primary, CancellationToken cancellationToken) => ValueTask.FromResult<UiDriverWindow>(new Window(key));

        internal override void Pump()
        {
        }

        internal override Task<bool> DrawAsync(UiDriverWindow window, DrawList2D commands)
        {
            Draws++;
            if (commands.Commands.Count == 1 && ReferenceEquals(_previous, commands))
            {
                Reuses++;
                if (Reuses >= 3)
                {
                    Reused.TrySetResult();
                }
            }

            _previous = commands;
            return Task.FromResult(true);
        }

        internal override ValueTask CloseWindowAsync(UiDriverWindow window) => window.DisposeAsync();
    }

    private sealed class Window(string key) : UiDriverWindow(key)
    {
        private readonly Guid _scope = Guid.NewGuid();
        private bool _visible;
        private bool _closed;

        public override ValueTask DisposeAsync()
        {
            _closed = true;
            return ValueTask.CompletedTask;
        }

        internal override UiWindowSourceSnapshot CaptureState() => new(1, null, new(20, 10), Vector2.One, _visible, _visible, _closed, _scope);

        internal override void Show() => _visible = true;

        internal override void SetTitle(string title)
        {
        }
    }
}
