using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Middleware;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;
using WebRecomp.WorkoutIntelligence;
using WebRecomp.WorkoutIntelligence.Persistence;
using WebRecomp.CatalogSync;
using WebRecomp.CatalogSync.Providers;
using WebRecomp.Web.Services.CatalogSync;
using WebRecomp.Web.Services.Nutrition;
using WebRecomp.Web.Services.Learning;
using WebRecomp.NutritionIntelligence.Contracts;
using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.NutritionIntelligence.Providers.FatSecret;
using WebRecomp.NutritionIntelligence.Providers.Usda;

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

    // Cloudflare edge IPs can change. Accept only known 12WR hosts when
    // honoring the forwarded host supplied by our edge.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.AllowedHosts.Add("test.12weekrecomp.com");
    options.AllowedHosts.Add("12weekrecomp.com");
    options.AllowedHosts.Add("www.12weekrecomp.com");
    options.AllowedHosts.Add("recomp-test.azurewebsites.net");
    options.AllowedHosts.Add("recomp-live.azurewebsites.net");
});

// Native 12WR cookie authentication is the only interactive sign-in path.
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme);

builder.Services.Configure<CookieAuthenticationOptions>(
    CookieAuthenticationDefaults.AuthenticationScheme,
    options =>
    {
        options.LoginPath = "/Account/SignIn";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "12WR.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, CanonicalRoleClaimsTransformation>();

builder.Services.AddControllersWithViews();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("fitdays-preview", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        httpContext.User.Identity?.Name ?? httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true }));

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

    options.AddPolicy("local-auth", httpContext =>
    {
        var subject =
            httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: subject,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 12,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

builder.Services.AddDbContext<RecompDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));

// Verify the migration-owned local-auth schema at startup. The runtime web
// identity intentionally performs no DDL.
builder.Services.AddHostedService<LocalAuthSchemaHostedService>();
builder.Services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
builder.Services.AddScoped<LocalAuthService>();
builder.Services.AddSingleton<LocalPasswordResetTokenService>();
builder.Services.AddScoped<LocalPasswordResetService>();
builder.Services.AddHostedService<TestDuplicateBootstrapAccountCleanupService>();

// v1011 Nutrition Intelligence composition root.
// Nutrition Intelligence remains independent; Web owns provider wiring.
builder.Services.AddSingleton(_ => FatSecretOptions.FromEnvironment());
builder.Services.AddHttpClient<FatSecretFoodProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("12WeekRecomp-Nutrition/1.0");
});
builder.Services.AddTransient<IFoodCompositionProvider>(
    services => services.GetRequiredService<FatSecretFoodProvider>());

builder.Services.AddSingleton(_ => UsdaFoodDataCentralOptions.FromEnvironment());
builder.Services.AddHttpClient<UsdaFoodDataCentralProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("12WeekRecomp-Nutrition/1.0");
});
builder.Services.AddTransient<IFoodCompositionProvider>(
    services => services.GetRequiredService<UsdaFoodDataCentralProvider>());
builder.Services.AddScoped<INutritionFoodLogStore, SqlNutritionFoodLogStore>();
builder.Services.AddScoped<NutritionFoodSearchService>();
builder.Services.AddHostedService<NutritionProviderCacheMaintenanceService>();

builder.Services.AddScoped<FirstSignInWelcomeEmailService>();
builder.Services.AddScoped<AppUserContextService>();
builder.Services.AddMemoryCache();
builder.Services.AddDataProtection();
builder.Services.AddSingleton<WebRecomp.Web.Services.ExternalConnections.DeviceCredentialProtector>();
builder.Services.AddSingleton(new WebRecomp.Web.Services.ExternalConnections.DeviceConnectionStore(
    Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? builder.Environment.ContentRootPath, "data", "external-connections")));
builder.Services.AddScoped<WebRecomp.Web.Services.ExternalConnections.DeviceIntegrationService>();
builder.Services.AddHttpClient<WebRecomp.ExternalConnections.Fitdays.FitdaysClient>(client => client.Timeout = TimeSpan.FromSeconds(25))
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate })
    .RemoveAllLoggers(); // Fitdays places session tokens in its request query string.
builder.Services.AddScoped<AccessEntitlementService>();
builder.Services.AddScoped<PendingAccessCodeService>();
builder.Services.AddScoped<ConfigurationService>();
builder.Services.AddScoped<WorkoutActualService>();
builder.Services.AddScoped<WebRecomp.Web.Services.WorkoutTargeting.WorkoutTargetService>();
builder.Services.AddSingleton<IWorkoutIntelligenceGateway, WorkoutIntelligenceGateway>();
builder.Services.AddScoped<IExerciseCatalogStore, SqlExerciseCatalogStore>();
builder.Services.AddScoped<IDetailedWorkoutStore, SqlDetailedWorkoutStore>();
builder.Services.AddHttpClient<KineticExerciseCatalogProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("12WeekRecomp/1.0");
});
builder.Services.AddTransient<IExerciseCatalogProvider>(
    services => services.GetRequiredService<KineticExerciseCatalogProvider>());
builder.Services.AddSingleton<IExerciseMediaProvider, NoExerciseMediaProvider>();
builder.Services.AddScoped<ICatalogSyncStore, SqlCatalogSyncStore>();
builder.Services.AddScoped<CatalogSyncCoordinator>();
builder.Services.AddHostedService<CatalogSyncHostedService>();
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
builder.Services.AddScoped<Engine30DashboardAnalysisService>();
builder.Services.AddScoped<Engine30AdaptiveRecommendationService>();
builder.Services.AddScoped<Engine30TrainingLoadRecoveryService>();
builder.Services.AddScoped<IRecompRecommendationEngine, Engine30RecompRecommendationEngine>();
builder.Services.AddSingleton<Engine30FuelStateService>();
builder.Services.AddSingleton<Engine30WeeklyFuelProjectionService>();
builder.Services.AddScoped<Engine30AdminFuelComparisonService>();
builder.Services.AddScoped<WeeklyPlanningAssessmentService>();
builder.Services.AddScoped<ProgramPlanService>();
builder.Services.AddScoped<WeeklyPlanService>();
builder.Services.AddScoped<DashboardService>();
builder.Services.AddScoped<WebRecomp.Web.Services.Dashboard3.RecompCockpitService>();
builder.Services.AddScoped<LearningOrchestratorService>();
builder.Services.AddHostedService<LearningSchemaHostedService>();
builder.Services.AddHttpClient<IEmailService, MicrosoftGraphEmailService>();

// v1006 demo infrastructure.
builder.Services.AddScoped<DemoSessionService>();
builder.Services.AddScoped<GuidedDemoSeederService>();

// TEST persona infrastructure remains available for deliberate diagnostics,
// but persistent synthetic accounts are no longer seeded automatically.
builder.Services.AddScoped<TestPersonaSessionService>();
builder.Services.AddScoped<TestPersonaSeederService>();
builder.Services.AddScoped<TestPersonaNutritionSeederService>();

if (builder.Configuration.GetValue<bool>("WebRecomp:EnableTestPersonas"))
{
    builder.Services.AddHostedService<TestPersonaLegacyMigrationHostedService>();
    builder.Services.AddHostedService<TestPersonaSeederHostedService>();
    builder.Services.AddHostedService<TestPersonaNutritionSeederHostedService>();
}

builder.Services.AddHealthChecks();
builder.Services.AddProblemDetails();

var app = builder.Build();

app.UseExceptionHandler("/TestDiagnostics/Error");

// Must be before HTTPS redirection and authentication.
app.UseForwardedHeaders();

app.UseHttpsRedirection();

// Static asset provider: explicitly support modern marketing image formats.
// This avoids a silent 404 on environments whose default provider does not
// include WebP, which would leave image-led pages visually blank.
var staticContentTypes = new FileExtensionContentTypeProvider();
staticContentTypes.Mappings[".webp"] = "image/webp";
app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = staticContentTypes
});

app.UseRouting();

app.UseAuthentication();

// A permanent 12WR cookie wins. Only anonymous requests are eligible for
// restoration from temporary TEST/demo cookies.
app.UseMiddleware<DemoSessionMiddleware>();
app.UseMiddleware<TestPersonaMiddleware>();

app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
