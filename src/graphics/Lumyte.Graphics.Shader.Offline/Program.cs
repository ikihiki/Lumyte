using Lumyte.Graphics.Shader.Offline;

if (args.Length != 6 || args[0] != "--source" || args[2] != "--output" || args[4] != "--cache")
{
    Console.Error.WriteLine("Usage: Lumyte.Graphics.Shader.Offline --source <file> --output <file> --cache <directory>");
    return 2;
}

try
{
    string compiler = await SlangCompilerLocator.ResolveAsync(args[5]);
    await SlangPackageCompiler.CompileAsync(compiler, args[1], args[3]);
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
