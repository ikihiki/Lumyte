using Lumyte.Input;
using Xunit;

namespace Lumyte.Platform.Windows.Tests;

public sealed class WindowsTextInputContextTests
{
    [Fact]
    public void ActivationAndFallbackCharactersEditTheClient()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var platform = new WindowsPlatform();
        using WindowsWindow window = platform.CreateWindow(new() { IsVisible = false });
        WindowsTextInputContext textInput = platform.Input.GetWindow(window).TextInput;
        var client = new FakeTextInputClient();
        textInput.Activate(client);

        textInput.DispatchCharacter('A');
        textInput.DispatchCharacter('\ud83d');
        textInput.DispatchCharacter('\ude80');

        Assert.True(textInput.IsActive);
        Assert.Equal("A🚀", client.Text);
        Assert.Equal(new TextRange(3, 0), client.Selection);
    }

    [Fact]
    public void DeactivationClearsCompositionAndCandidates()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var platform = new WindowsPlatform();
        using WindowsWindow window = platform.CreateWindow(new() { IsVisible = false });
        WindowsTextInputContext textInput = platform.Input.GetWindow(window).TextInput;
        var client = new FakeTextInputClient();
        client.SetComposition(new(2, 4), new(3, 1));
        client.SetCandidates(new()
        {
            Items = ["first", "second"],
            SelectedIndex = 1,
            PageStart = 0,
            PageSize = 2,
        });
        textInput.Activate(client);

        textInput.Deactivate();

        Assert.False(textInput.IsActive);
        Assert.Equal(default, client.Composition);
        Assert.Null(client.CompositionTarget);
        Assert.Null(client.Candidates);
    }

    [Fact]
    public void ClientNotificationsAreAcceptedWhileActive()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var platform = new WindowsPlatform();
        using WindowsWindow window = platform.CreateWindow(new() { IsVisible = false });
        WindowsTextInputContext textInput = platform.Input.GetWindow(window).TextInput;
        textInput.Activate(new FakeTextInputClient());

        textInput.NotifyTextChanged(new(0, 0, 1));
        textInput.NotifySelectionChanged();
        textInput.NotifyLayoutChanged();

        Assert.True(textInput.IsActive);
    }
}
