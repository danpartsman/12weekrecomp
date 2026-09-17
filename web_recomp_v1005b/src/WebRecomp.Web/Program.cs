using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;

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

builder.Services.AddDbContext<RecompDbContext>(options =>
    options.UseSqlServer(connectionString, sql =>
        sql.EnableRetryOnFailure(5, TimeSpan.FromSeconds(10), null)));

builder.Services.AddScoped<AppUserContextService>();
builder.Services.AddScoped<ConfigurationService>();
builder.Services.AddScoped<WorkoutMathService>();
builder.Services.AddScoped<WeeklyStrategyService>();
builder.Services.AddScoped<ProgramPlanService>();
builder.Services.AddScoped<WeeklyPlanService>();
builder.Services.AddScoped<RecommendationService>();
builder.Services.AddScoped<DashboardService>();

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
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();
app.MapControllers();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();