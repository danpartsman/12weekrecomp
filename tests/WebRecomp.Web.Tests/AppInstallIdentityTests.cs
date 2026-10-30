using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using WebRecomp.Web.Middleware;
using Xunit;
namespace WebRecomp.Web.Tests;
public sealed class AppInstallIdentityTests
{
    [Theory]
    [InlineData("test.12weekrecomp.com",true)]
    [InlineData("recomp-test.azurewebsites.net",true)]
    [InlineData("12weekrecomp.com",false)]
    [InlineData("recomp-live.azurewebsites.net",false)]
    public async Task ManifestLabelsEnvironmentAndPreservesInstalledIdentity(string host,bool test)
    {
        var config=new ConfigurationBuilder().Build();var context=new DefaultHttpContext();context.Request.Host=new(host);context.Request.Method="GET";context.Request.Path="/manifest.webmanifest";context.Response.Body=new MemoryStream();
        await new AppInstallMiddleware(_=>throw new Exception("Manifest should be handled before static files."),config).InvokeAsync(context);
        context.Response.Body.Position=0;using var json=await JsonDocument.ParseAsync(context.Response.Body);var root=json.RootElement;
        Assert.Equal(test?"12WR TEST":"12WR",root.GetProperty("short_name").GetString());Assert.Equal("/",root.GetProperty("id").GetString());Assert.Equal("/",root.GetProperty("start_url").GetString());Assert.Equal(test?"/favicon-test.svg":"/favicon.svg",root.GetProperty("icons")[0].GetProperty("src").GetString());Assert.Equal("no-cache",context.Response.Headers.CacheControl.ToString());Assert.Equal("application/manifest+json",context.Response.ContentType);
    }
    [Fact]
    public void AzureSiteNameIdentifiesTestOnAlternateHost()
    {
        var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["WEBSITE_SITE_NAME"]="recomp-test"}).Build();Assert.True(AppInstallIdentity.IsTest(config,"alternate.azurewebsites.net"));
    }
}
