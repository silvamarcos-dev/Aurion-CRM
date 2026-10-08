using System.Text.Json.Serialization;
using Loteadora.Crm.Api.Endpoints;
using Loteadora.Crm.Api.Infrastructure;
using Loteadora.Crm.Modules;
using Microsoft.AspNetCore.HttpOverrides;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCrmModules(builder.Configuration);
builder.Services.AddCrmSecurity(builder.Configuration);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CrmExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();
builder.Services.AddScoped<DevelopmentSeeder>();

var app = builder.Build();

// Confia em X-Forwarded-For somente de proxies conhecidos (padrão: loopback, onde roda o BFF em dev).
// Em produção, configurar KnownProxies/KnownNetworks do BFF/ingress.
app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });
app.UseExceptionHandler();
app.UseSecurityHeaders();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await app.Services.MigrateAndSeedDevelopmentAsync(app.Configuration);
}
else
{
    app.UseHsts();
}

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();

var api = app.MapGroup("/api/v1").RequireRateLimiting(SecurityRegistration.UserRateLimitPolicy);
api.MapAuthEndpoints();
api.MapDirectoryEndpoints();
api.MapLeadEndpoints();
api.MapPipelineEndpoints();
api.MapCalendarEndpoints();
api.MapSchedulingEndpoints();
api.MapTaskEndpoints();
api.MapMacroEndpoints();
api.MapAccessLinkEndpoints();
api.MapSuggestionEndpoints();
api.MapDevToolsEndpoints();

await app.RunAsync();

public partial class Program;
