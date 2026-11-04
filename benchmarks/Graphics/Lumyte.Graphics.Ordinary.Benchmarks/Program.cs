using Lumyte.Graphics.Gpu.Benchmarks;

try
{
    if (args.SequenceEqual(new[] { "--smoke" }))
    {
        OrdinarySmoke.Verify();
        Console.WriteLine("Ordinary deterministic CPU fixture checks passed; no GPU coverage claimed.");
        return 0;
    }

    return await OrdinaryRunner.RunAsync(args);
}
catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
{
    Console.Error.WriteLine(error.Message);
    return 2;
}
