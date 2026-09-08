using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using TwelveWeekRecomp.Live.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.AddAntiforgery(options =>
{
    options.Cookie.Name = "__Host-12wr-xsrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddSingleton<LaunchInterestRepository>();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("launch-join", httpContext =>
    {
        var key = ClientKey(httpContext);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 6,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    });
});

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseRateLimiter();

app.MapRazorPages();

app.MapGet("/health", (IConfiguration config) =>
{
    return Results.Ok(new
    {
        status = "ok",
        siteMode = config["SiteMode"] ?? "UNKNOWN",
        utc = DateTimeOffset.UtcNow
    });
});

app.Run();

static string ClientKey(HttpContext context)
{
    var cf = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(cf))
        return "cf:" + TrimKey(cf);

    var forwarded = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
    if (!string.IsNullOrWhiteSpace(forwarded))
    {
        var first = forwarded.Split(',')[0].Trim();
        if (!string.IsNullOrWhiteSpace(first))
            return "xff:" + TrimKey(first);
    }

    return "ip:" + TrimKey(context.Connection.RemoteIpAddress?.ToString() ?? "unknown");
}

static string TrimKey(string value)
{
    value = value.Trim();
    return value.Length <= 80 ? value : value[..80];
}
