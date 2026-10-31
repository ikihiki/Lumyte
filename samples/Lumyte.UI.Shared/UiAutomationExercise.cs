using Lumyte.Graphics.Drawing2D;
using Lumyte.Input.Abstractions;
using Lumyte.UI.Automation;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.SampleSupport;

internal static class UiAutomationExercise
{
    internal static UiSurface CreateSurface(Action? clicked = null)
    {
        var saved = new UiSavedIndicator();
        return new(Panel()[Stack(spacing: 8)[Button(testId: "save", automationName: "保存", width: 160, height: 80, onClick: Save)[Rectangle(width: 160, height: 80, fill: new Color2D(0.2f, 0.5f, 0.9f, 1))], saved]]);

        void Save()
        {
            saved.Show();
            clicked?.Invoke();
        }
    }

    internal static async Task RunAsync(IUiAutomationHost host)
    {
        var page = new UiAutomationPage(host, new() { Timeout = TimeSpan.FromSeconds(30) });
        UiLocator save = page.GetByRole(UiRole.Button, name: "保存");
        await save.ClickAsync();
        await page.Expect(page.GetByTestId("saved-indicator")).ToBeVisibleAsync();
        await save.PressAsync(Key.Enter);
        await page.Expect(save).ToBeFocusedAsync();
        Console.WriteLine("UI automation passed: Save click, visible indicator and Enter activation.");
    }
}
