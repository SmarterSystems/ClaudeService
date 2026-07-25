using SmarterSystems.Claude.Service.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddSingleton<IClaudeCliRunner, ClaudeCliRunner>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseMiddleware<ApiKeyMiddleware>();
app.MapControllers();
app.Run();
