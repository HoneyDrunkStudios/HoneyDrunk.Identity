using HoneyDrunk.Audit.Data;
using HoneyDrunk.Data.Abstractions.Transactions;
using HoneyDrunk.Identity;
using HoneyDrunk.Identity.Abstractions;
using HoneyDrunk.Identity.Providers.Entra;
using HoneyDrunk.Kernel.Abstractions.Identity;
using HoneyDrunk.Kernel.Hosting;
using HoneyDrunk.Kernel.Telemetry;
using HoneyDrunk.Telemetry.OpenTelemetry.Extensions;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHoneyDrunkNode(options =>
{
    options.NodeId = new NodeId("honeydrunk-identity");
    options.SectorId = new SectorId("core");
    options.EnvironmentId = new EnvironmentId(builder.Environment.EnvironmentName.ToLowerInvariant());
    options.StudioId = "honeydrunk-studios";
});
builder.Services.AddDbContextFactory<IdentityDbContext>(options => options.UseSqlServer(
    builder.Configuration.GetConnectionString("identity") ?? throw new InvalidOperationException("Identity SQL connection is required.")));
builder.Services.AddScoped<IUserDirectory, SqlUserDirectory>();
builder.Services.AddScoped<IUnitOfWork<IAuditDataContext>, IdentityAuditUnitOfWork>();
builder.Services.AddHoneyDrunkAuditData();
builder.Services.AddHoneyDrunkTelemetry();
if (!builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHoneyDrunkOpenTelemetry(options =>
    {
        options.ServiceName = "honeydrunk-identity";
        options.Environment = builder.Environment.EnvironmentName;
        options.OtlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";
        options.AdditionalActivitySources.Add("HoneyDrunk.Data");
    });
}

builder.Services.AddEntraValidation();
builder.Services.AddAuthorization();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins("http://localhost:8081", "http://127.0.0.1:8081").AllowAnyHeader().AllowAnyMethod()));
var app = builder.Build();
app.UseCors();

// Public clients cannot assert internal Grid ownership or arbitrary baggage.
app.Use(async (context, next) =>
{
    foreach (var header in context.Request.Headers.Keys.Where(key =>
        key.StartsWith("X-Baggage-", StringComparison.OrdinalIgnoreCase) || new[] { "X-Tenant-Id", "X-Project-Id", "X-Causation-Id", "baggage" }.Contains(key, StringComparer.OrdinalIgnoreCase)).ToArray())
    {
        context.Request.Headers.Remove(header);
    }

    context.Request.Headers["X-Correlation-Id"] = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
    await next(context);
});
app.UseGridContext();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", async (IdentityDbContext db, CancellationToken token) => await db.Database.CanConnectAsync(token) ? Results.Ok() : Results.StatusCode(503));
app.MapGet("/users/me", async (ClaimsPrincipal principal, IUserDirectory directory, CancellationToken token) =>
{
    try
    {
        return Results.Ok(await directory.Resolve(new ExternalSubject(principal.FindFirstValue("iss")!, principal.FindFirstValue("sub")!), token));
    }
    catch (UnauthorizedAccessException)
    {
        return Results.Unauthorized();
    }
}).RequireAuthorization();
app.MapGet("/client-configuration", () =>
{
    var authority = builder.Configuration["Entra:Authority"];
    var clientId = builder.Configuration["Entra:MobileClientId"];
    var scope = builder.Configuration["Entra:ApiScope"];
    var issuer = builder.Configuration["Entra:Issuer"];
    var audience = builder.Configuration["Entra:Audience"];
    return string.IsNullOrWhiteSpace(authority) || string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(scope)
        || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience)
        ? Results.Problem("Sign-in configuration is not ready.", statusCode: 503)
        : Results.Ok(new { authority, clientId, scope, signInChoices = new[] { "Apple", "Google", "Microsoft" } });
});
await app.RunAsync();
