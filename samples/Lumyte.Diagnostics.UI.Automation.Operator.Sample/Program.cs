using System.Net.Http.Headers;
using System.Text.Json;
using Lumyte.Diagnostics.Transport;
using Lumyte.Diagnostics.UI.Automation.Client;
using Lumyte.Diagnostics.UI.Automation.Contracts;

var address = new Uri(args.Length > 0 ? args[0] : "http://127.0.0.1:5000");
string output = args.Length > 1 ? args[1] : "ui-remote.png";
string token = Environment.GetEnvironmentVariable("LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN") ?? throw new InvalidOperationException("Set the operator token.");
using var http = new HttpClient { BaseAddress = address };
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
SessionSnapshot? session = null;
while (session is null)
{
    using HttpResponseMessage response = await http.GetAsync("diagnostics/v1/sessions", deadline.Token);
    response.EnsureSuccessStatusCode();
    SessionSnapshot[] sessions = JsonSerializer.Deserialize(await response.Content.ReadAsByteArrayAsync(deadline.Token), DiagnosticJson.Context.SessionSnapshotArray)!;
    session = sessions.FirstOrDefault(candidate => candidate.Catalog.Any(subsystem => subsystem.Subsystem.Id == "ui.automation"));
    if (session is null)
    {
        await Task.Delay(250, deadline.Token);
    }
}

await using (var client = new UiAutomationDiagnosticsClient(http, session.SessionId, new() { JobTimeoutMilliseconds = 10000 }))
{
    UiRemoteTarget target = (await client.ListTargetsAsync(deadline.Token)).Single(candidate => candidate.Name == "settings");
    await using (UiRemoteControl control = await client.AcquireControlAsync(target.Id, deadline.Token))
    {
        UiRemoteLocator save = control.Page.GetByRole("button", name: "保存");
        await save.ClickAsync(cancellationToken: deadline.Token);
        await control.Page.Expect(control.Page.GetByTestId("saved-indicator")).ToBeVisibleAsync(deadline.Token);
        await save.PressAsync("Enter", cancellationToken: deadline.Token);
        if (await save.CountAsync(deadline.Token) != 1)
        {
            throw new InvalidOperationException("The saved button was not uniquely located.");
        }

        if (target.AllowScreenshots)
        {
            await File.WriteAllBytesAsync(output, await control.Page.ScreenshotAsync(deadline.Token), deadline.Token);
            Console.WriteLine("Verified remote PNG saved.");
        }
    }

    Console.WriteLine("Remote UI automation passed: discovery, control, Click, indicator Expect, Enter, count, cleanup.");
}

if (Environment.GetEnvironmentVariable("LUMYTE_UI_CLOSE_SAMPLE") == "1")
{
    using HttpResponseMessage response = await http.DeleteAsync($"diagnostics/v1/sessions/{session.SessionId:D}/connection", deadline.Token);
    response.EnsureSuccessStatusCode();
}
