using Lumyte.DevTools.Agent;
using Lumyte.DevTools.Server;

using MagicOnion.Server;

using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
string pipeName = builder.Configuration["DevTools:PipeName"] ?? DevToolsAgent.DefaultPipeName;
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(5198, listen => listen.Protocols = HttpProtocols.Http1);
    options.ListenNamedPipe(pipeName, listen => listen.Protocols = HttpProtocols.Http2);
});
builder.Services.AddSingleton<DevToolsHostRegistry>();
builder.Services.AddMagicOnion();
WebApplication app = builder.Build();
app.UseDefaultFiles();app.UseStaticFiles();app.UseWebSockets();
app.MapMagicOnionService();
app.Map("/devtools", context => DevToolsRemoteWebSocketEndpoint.HandleAsync(context, app.Services.GetRequiredService<DevToolsHostRegistry>()));
app.Run();
