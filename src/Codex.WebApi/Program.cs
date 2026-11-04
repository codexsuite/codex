using System.Reflection;
using System.Text.Json.Serialization;
using Agoda.IoC.NetCore;
using Codex.Application;
using Codex.Contracts;
using Codex.WebApi;
using Codex.WebApi.Features.Auth;
using Codex.WebApi.Infrastructure;
using Codex.WebApi.Infrastructure.Telemetry;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog replaces the logger factory, so levels live under Serilog:MinimumLevel
// rather than Logging:LogLevel. The console sink keeps `docker logs` working;
// Seq is posted to directly only when CODEX_SEQ_URL is set, so tests and local
// dev never reach for it. Configured from the service provider, not here, because
// WebApplicationFactory-based tests append their config overrides after this
// top-level code has run.
// preserveStaticLogger keeps each host's logger its own, so one test host being
// disposed can't close the static logger another is still writing through.
builder.Services.AddSerilog((services, logger) =>
{
    var configuration = services.GetRequiredService<IConfiguration>();
    logger.ReadFrom.Configuration(configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "codex")
        .WriteTo.Console();
    var seqUrl = configuration["CODEX_SEQ_URL"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
    {
        logger.WriteTo.Seq(seqUrl, apiKey: configuration["CODEX_SEQ_API_KEY"]);
    }
    logger.AddOtlpSink(configuration);
}, preserveStaticLogger: true);

Assembly[] moduleAssemblies = [ApiMarker.Assembly, ApplicationMarker.Assembly];
builder.Services.AutoWireAssembly(moduleAssemblies, isMockMode: false);
foreach (var module in ModuleScan.Find<IServiceModule>(moduleAssemblies))
{
    module.AddServices(builder.Services, builder.Configuration);
}
// Framework registration: the AGENTS.md "no services.Add* in Program.cs" rule is
// about our own types. Tests swap in a FakeTimeProvider.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddControllers()
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
// Minimal API endpoints (the feature modules) serialize exactly like controllers: web defaults plus enums as names.
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();

// Behind Cloudflare: the forwarded proto and host (AuthServices) make the cookie Secure and redirects https.
app.UseForwardedHeaders();
// SPA assets ship inside wwwroot (built there by src/Codex.Client). Served before authorization, so the sign-in page's
// scripts and styles load without a session.
app.UseStaticFiles();
// Sign-in (#29, Features/Auth): every endpoint needs a signed-in user unless it is marked AllowAnonymous.
app.UseAuthentication();
app.UseAuthorization();
app.Use((context, next) =>
{
    CrossSiteWriteGuard.Check(context.Request);
    return next(context);
});

foreach (var module in ModuleScan.Find<IEndpointModule>(moduleAssemblies))
{
    module.MapEndpoints(app);
}
app.MapControllers();
// Unknown routes fall back to the SPA for client-side routing, except under
// /api, which must keep returning 404 rather than the SPA shell.
app.MapFallback("/api/{**path}", () => Results.NotFound());
app.MapFallbackToFile("index.html");

await app.RunAsync();
