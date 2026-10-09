using Lumyte.Diagnostics.Server;

WebApplication app = DiagnosticServerApplication.Create(args, options =>
{
    options.AllowedOrigins = (Environment.GetEnvironmentVariable("LUMYTE_DIAGNOSTICS_ALLOWED_ORIGINS") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    options.GameToken = Environment.GetEnvironmentVariable("LUMYTE_DIAGNOSTICS_GAME_TOKEN") ?? string.Empty;
    options.OperatorToken = Environment.GetEnvironmentVariable("LUMYTE_DIAGNOSTICS_OPERATOR_TOKEN") ?? string.Empty;
    options.HttpPort = int.Parse(Environment.GetEnvironmentVariable("LUMYTE_DIAGNOSTICS_HTTP_PORT") ?? "5000", System.Globalization.CultureInfo.InvariantCulture);
    options.MagicOnionPort = int.Parse(Environment.GetEnvironmentVariable("LUMYTE_DIAGNOSTICS_GRPC_PORT") ?? "5001", System.Globalization.CultureInfo.InvariantCulture);
});
await app.RunAsync();
