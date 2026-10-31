using Lumyte.Samples;
using Lumyte.Samples.Framework;
using Spectre.Console;

try
{
    var options = SampleOptions.Parse(args);
    if (options.Help)
    {
        Console.WriteLine("dotnet run --project samples/Lumyte.Samples -- [--sample ID] [--backend ID] [--configuration Debug|Release] [--port 8000] [--list] [-- SAMPLE_ARGS]");
        return 0;
    }

    if (options.List)
    {
        Table table = new Table().AddColumn("Sample").AddColumn("Role").AddColumn("Backends");
        foreach (SampleDefinition entry in SampleCatalog.All)
        {
            table.AddRow(Markup.Escape(entry.Id), Markup.Escape(entry.Role), Markup.Escape(string.Join(", ", entry.Backends.Select(b => b.Id + (b.IsAvailable ? string.Empty : " (Windows only)")))));
        }

        AnsiConsole.Write(table);
        return 0;
    }

    (SampleDefinition sample, SampleBackend backend) = SampleSelector.Select(SampleCatalog.All, options, AnsiConsole.Console);
    string root = SampleHost.FindRoot(AppContext.BaseDirectory);
    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };
    return await SampleHost.RunAsync(root, sample, backend, options, cancellation.Token);
}
catch (OperationCanceledException)
{
    return 130;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
