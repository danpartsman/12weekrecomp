using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.StaticFiles;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using WebRecomp.Web.Data;
using WebRecomp.Web.Middleware;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Engine30;
using WebRecomp.Web.Services.WorkoutIntelligence;
using WebRecomp.WorkoutIntelligence;
using WebRecomp.WorkoutIntelligence.Persistence;
using WebRecomp.CatalogSync;
using WebRecomp.CatalogSync.Providers;
using WebRecomp.Web.Services.CatalogSync;
using WebRecomp.Web.Services.Nutrition;
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

    // Cloudflare edge IPs can change. Restrict the accepted forwarded host
    // to our test hostname.
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.AllowedHosts.Add("test.12weekrecomp.com");
});

// Native 12WR cookie authentication is now the normal customer path. Entra
// remains registered only as a compatibility/staff sign-in option while
// existing external accounts are migrated deliberately.
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultSignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddMicrosoftIdentityWebApp(
        builder.Configuration.GetSection("AzureAd"));

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

// The idempotent local-auth schema bootstrap must run before TEST seeders that
// query customer records. It only adds the credential table; existing customer,
// client, program, workout and nutrition data are untouched.
builder.Services.AddHostedService<LocalAuthSchemaHostedService>();
builder.Services.AddScoped<IPasswordHasher<UserAccount>, PasswordHasher<UserAccount>>();
builder.Services.AddScoped<LocalAuthService>();

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
builder.Services.AddScoped<AccessEntitlementService>();
builder.Services.AddScoped<PendingAccessCodeService>();
builder.Services.AddScoped<ConfigurationService>();
builder.Services.AddScoped<WorkoutMathService>();
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

// TEST-only persistent synthetic personas. Both seeders and the session service
// independently refuse to operate outside the recomp-test app.
builder.Services.AddScoped<TestPersonaSessionService>();
builder.Services.AddHostedService<TestPersonaLegacyMigrationHostedService>();
builder.Services.AddScoped<TestPersonaSeederService>();
builder.Services.AddHostedService<TestPersonaSeederHostedService>();
builder.Services.AddScoped<TestPersonaNutritionSeederService>();
builder.Services.AddHostedService<TestPersonaNutritionSeederHostedService>();

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

// A permanent 12WR cookie or a legacy Entra cookie wins. Only anonymous
// requests are eligible for restoration from temporary TEST/demo cookies.
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
