using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using WebRecomp.Web.Data;
using WebRecomp.Web.Middleware;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("RecompDatabase")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:RecompDatabase is not configured.");

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedHost |
        ForwardedHeaders.XForwardedProto;

    // Our Cloudflare Worker supplies these.
    options.ForwardedHostHeaderName = "X-Original-Host";
    options.ForwardedProtoHeaderName = "X-Forwarded-Proto";

    // Cloudflare edge IPs can change. Restrict the accepted forwarded host
    // to our test hostname.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.AllowedHosts.Add("test.12weekrecomp.com");
});

builder.Services
    .AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(
        builder.Configuration.GetSection("AzureAd"));

builder.Services.Configure<OpenIdConnectOptions>(
    OpenIdConnectDefaults.AuthenticationScheme,
    options =>
    {
        options.Events.OnRedirectToIdentityProviderForSignOut = context =>
        {
            var loginHint = context.HttpContext.User.FindFirst("login_hint")?.Value;

            if (!string.IsNullOrWhiteSpace(loginHint))
            {
                context.ProtocolMessage.SetParameter("logout_hint", loginHint);
            }

            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddControllersWithViews();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("access-code", httpContext =>
    {
        var subject =
            httpContext.User.FindFirst("oid")?.Value
            ?? httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: subject,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

builder.Services.AddDbContext<RecompDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));

builder.Services.AddScoped<AppUserContextService>();
builder.Services.AddScoped<AccessEntitlementService>();
builder.Services.AddScoped<PendingAccessCodeService>();
builder.Services.AddScoped<ConfigurationService>();
builder.Services.AddScoped<WorkoutMathService>();
builder.Services.AddScoped<Engine30WorkoutProfileService>();
builder.Services.AddScoped<Engine30WorkoutUiCatalogService>();
builder.Services.AddScoped<IEngine30WorkoutRuntimeProfileProvider, Engine30WorkoutRuntimeProfileProvider>();
builder.Services.AddScoped<Engine30WorkoutRuntimeService>();
builder.Services.AddScoped<Engine30DailyRuntimeService>();
builder.Services.AddScoped<Engine30ClientActivityProfileService>();
builder.Services.AddScoped<Engine30PrescriptionService>();
builder.Services.AddScoped<Engine30GoalEnergyPolicyService>();
builder.Services.AddSingleton<Engine30RecompEnergyPolicyAllocator>();
builder.Services.AddSingleton<Engine30EnergyCarryoverService>();
builder.Services.AddScoped<Engine30DashboardProjectionService>();
builder.Services.AddSingleton<Engine30FuelStateService>();
builder.Services.AddSingleton<Engine30WeeklyFuelProjectionService>();
builder.Services.AddScoped<Engine30AdminFuelComparisonService>();
builder.Services.AddScoped<Engine21EnergyService>();
builder.Services.AddScoped<WeeklyStrategyService>();
builder.Services.AddScoped<ProgramPlanService>();
builder.Services.AddScoped<WeeklyPlanService>();
builder.Services.AddScoped<RecommendationService>();
builder.Services.AddScoped<RecompEngineService>();
builder.Services.AddScoped<RecompEngineConfigurationService>();
builder.Services.AddScoped<RecompTrainingPrescriptionService>();
builder.Services.AddScoped<RecompEngineOrchestrator>();
builder.Services.AddScoped<RecompActualWorkoutAnalysisService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddHttpClient<IEmailService, MicrosoftGraphEmailService>();

// v1006 demo infrastructure.
builder.Services.AddScoped<DemoSessionService>();
builder.Services.AddScoped<GuidedDemoSeederService>();

builder.Services.AddScoped<Engine21PipelineService>();
builder.Services.AddScoped<Engine21FuelWindowService>();
builder.Services.AddScoped<Engine21CarbRequirementService>();
builder.Services.AddScoped<Engine21MacroReconciliationService>();
builder.Services.AddScoped<Engine21DailyMacroService>();
builder.Services.AddScoped<Engine21ProjectedFuelWindowService>();
builder.Services.AddScoped<Engine21WeeklyMacroProjectionService>();
builder.Services.AddScoped<Engine21RealDataService>();
builder.Services.AddScoped<Engine21WeeklyEnergyService>();
builder.Services.AddScoped<Engine21DeficitService>();
builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler("/Home/Error");

// Must be before HTTPS redirection and authentication.
app.UseForwardedHeaders();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();

// Permanent Entra/OIDC identity wins. Only anonymous requests are eligible
// for restoration from the v1006 demo-session cookie.
app.UseMiddleware<DemoSessionMiddleware>();

app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

























