using Lumyte.Graphics.Portable.Shaders.Offline;

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
try
{
    await PortableShaderBuildCommand.RunAsync(args, cancellation.Token);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
