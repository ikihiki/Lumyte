#:property GenerateDocumentationFile=true

using System.Text.Json;

if (args.Length != 2)
{
    throw new ArgumentException("Expected the mise bootstrap JSON path and platform.");
}

using var configuration = JsonDocument.Parse(File.ReadAllText(args[0]));
string version = configuration.RootElement.GetProperty("version").GetString() ?? throw new InvalidDataException("The mise version is missing.");
string checksum = configuration.RootElement.GetProperty("platforms").GetProperty(args[1]).GetProperty("sha256").GetString() ?? throw new InvalidDataException("The mise checksum is missing.");
if (version.Length == 0 || version.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '+') ||
    checksum.Length != 64 || checksum.Any(character => !char.IsAsciiHexDigit(character)))
{
    throw new InvalidDataException("The mise version or SHA-256 checksum is invalid.");
}

Console.WriteLine(version + " " + checksum);
