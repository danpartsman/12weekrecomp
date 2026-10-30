using System.Text.Json;
namespace WebRecomp.Web.Middleware;

public static class AppInstallIdentity
{
    public static bool IsTest(IConfiguration configuration,string host)
        => string.Equals(configuration["WEBSITE_SITE_NAME"],"recomp-test",StringComparison.OrdinalIgnoreCase)
           || host.Equals("test.12weekrecomp.com",StringComparison.OrdinalIgnoreCase)
           || host.Equals("recomp-test.azurewebsites.net",StringComparison.OrdinalIgnoreCase);
    public static Dictionary<string,object> Manifest(bool test)=>new()
    {
        ["id"]="/",["name"]=test?"12 Week Recomp TEST":"12 Week Recomp",
        ["short_name"]=test?"12WR TEST":"12WR",["start_url"]="/",["scope"]="/",["display"]="standalone",
        ["background_color"]="#07131d",["theme_color"]=test?"#854d0e":"#07131d",
        ["description"]=test?"12 Week Recomp test environment":"12 Week Recomp",
        ["icons"]=new[]{new{src=test?"/favicon-test.svg":"/favicon.svg",sizes="any",type="image/svg+xml",purpose="any"}}
    };
}

/// <summary>Stable app identity per origin; environment-specific naming without replacing installs.</summary>
public sealed class AppInstallMiddleware(RequestDelegate next,IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if(context.Request.Path.Equals("/manifest.webmanifest",StringComparison.OrdinalIgnoreCase)
           &&(HttpMethods.IsGet(context.Request.Method)||HttpMethods.IsHead(context.Request.Method)))
        {
            context.Response.ContentType="application/manifest+json";
            context.Response.Headers.CacheControl="no-cache";
            if(!HttpMethods.IsHead(context.Request.Method))await context.Response.WriteAsync(JsonSerializer.Serialize(AppInstallIdentity.Manifest(AppInstallIdentity.IsTest(configuration,context.Request.Host.Host))));
            return;
        }
        await next(context);
    }
}
