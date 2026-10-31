using Lumyte.UI.TextEditing.Sample;

if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
{
    Console.Error.WriteLine("This sample requires Windows 10 and a font file argument.");
    return 1;
}

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: Lumyte.UI.TextEditing.Sample <font.ttf> [--custom-candidates]");
    return 1;
}

Exception? failure = null;
var owner = new Thread(() =>
{
    try
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
        {
            throw new PlatformNotSupportedException();
        }

        TextInputHost.Run(args[0], args.Contains("--custom-candidates", StringComparer.Ordinal));
    }
    catch (Exception error)
    {
        failure = error;
    }
});
owner.SetApartmentState(ApartmentState.STA);
owner.Start();
owner.Join();
if (failure is not null)
{
    Console.Error.WriteLine(failure);
    return 1;
}

return 0;
