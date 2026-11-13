using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using WebRecomp.Web.Services.Coaching;

namespace WebRecomp.Web.Tests;

public sealed class SnapRequestRateLimitTests
{
    [Fact]
    public async Task LimitsArePerUserEstimatesAreSeparateAndRejectionsExplainSafeManualRetry()
    {
        var processed = 0;
        using var server = new TestServer(new WebHostBuilder().ConfigureServices(services =>
        {
            services.AddRouting();
            services.AddRateLimiter(options => { options.RejectionStatusCode = 429; SnapRequestRateLimits.Configure(options); });
        }).Configure(app =>
        {
            app.UseRouting();
            app.Use(async (http, next) =>
            {
                http.Connection.RemoteIpAddress = IPAddress.Loopback;
                http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, http.Request.Headers["X-Actor"].ToString())], "test"));
                await next();
            });
            app.UseRateLimiter();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapPost("/snap/assign", async http => { processed++; await http.Response.WriteAsync("assigned"); }).RequireRateLimiting(SnapRequestRateLimits.Submissions);
                endpoints.MapPost("/snap/estimate", http => http.Response.WriteAsync("estimate")).RequireRateLimiting(SnapRequestRateLimits.Estimates);
            });
        }));
        using var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Actor", "coach-one");
        for (var i = 0; i < 60; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/snap/estimate", new StringContent(""))).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/snap/assign", new StringContent(""))).StatusCode);
        }
        var response = await client.PostAsync("/snap/assign", new FormUrlEncodedContent(new Dictionary<string,string>
        { ["Name"] = "Keep this workout <script>alert(1)</script>", ["__RequestVerificationToken"] = "retained-token", ["RequestId"] = "same-request" }));
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(60, processed);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("How to fix it", html); Assert.Contains("This submission was not processed", html);
        Assert.Contains("retained-token", html); Assert.Contains("same-request", html);
        Assert.Contains("&lt;script&gt;", html); Assert.DoesNotContain("<script>alert", html);
        Assert.Contains("Try submission again", html); Assert.Contains(" disabled", html);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/snap/estimate", new StringContent(""))).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Actor"); client.DefaultRequestHeaders.Add("X-Actor", "coach-two");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/snap/assign", new StringContent(""))).StatusCode);
        Assert.Equal(61, processed);
    }
}
