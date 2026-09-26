using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using SmarterSystems.Claude.Service.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<IClaudeCliRunner, ClaudeCliRunner>();

// nginx terminates TLS and forwards over loopback. Without this the app records
// 127.0.0.1 as the caller and http as the scheme for every request.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();
app.Run();
