using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using WebRecomp.Web.Services.SmartIntelligence;
namespace WebRecomp.Web.Tests;

public sealed class SmartIntelligenceAiTests
{
    private sealed class Host : IWebHostEnvironment
    {
        public string ApplicationName {get;set;}="test";public string EnvironmentName {get;set;}="test";
        public string ContentRootPath {get;set;}=Path.GetTempPath();public string WebRootPath {get;set;}=Path.GetTempPath();
        public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider();
    }
    private sealed class Handler : HttpMessageHandler
    {
        public int Calls;public string? Sent;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            Calls++;Sent=await request.Content!.ReadAsStringAsync(ct);
            Assert.Equal("https://api.openai.com/v1/responses",request.RequestUri!.ToString());
            Assert.Equal("Bearer",request.Headers.Authorization!.Scheme);
            var answer=JsonSerializer.Serialize(new {summary="Evidence review",confidence="Limited",findings=Array.Empty<object>(),limitations=new[]{"BIA uncertainty"}});
            return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new {status="completed",output=new[]{new {content=new[]{new {type="output_text",text=answer}}}}}),Encoding.UTF8,"application/json")};
        }
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory {public HttpClient CreateClient(string name)=>client;}
    [Fact] public async Task ProviderUsesStructuredReadOnlyRequestHonorsNotesAndIsolatesCacheByClient()
    {
        var configuration=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"SmartIntelligence:OpenAI:ApiKey","test-key-not-real"}}).Build();
        var connection=new IntelligenceConnection(configuration,new Host(),new EphemeralDataProtectionProvider());
        var handler=new Handler();using var http=new HttpClient(handler);using var cache=new MemoryCache(new MemoryCacheOptions());
        var provider=new OpenAiIntelligenceProvider(new Factory(http),connection,cache);
        var date=new DateOnly(2026,1,1);
        var report=IntelligenceStatistics.Build([new(){Date=date,Weight=210,RecoveryNote="private-recovery-note",FoodNote="private-food-note"}],date,date,"balanced","Skeletal muscle");
        var client=Guid.NewGuid();await provider.AnalyzeAsync(client,report,"felt tired",false,default);
        Assert.DoesNotContain("private-recovery-note",handler.Sent!);Assert.DoesNotContain("private-food-note",handler.Sent!);
        Assert.DoesNotContain(client.ToString(),handler.Sent!);Assert.DoesNotContain("test-key-not-real",handler.Sent!);
        using(var body=JsonDocument.Parse(handler.Sent!)){Assert.False(body.RootElement.GetProperty("store").GetBoolean());Assert.True(body.RootElement.GetProperty("text").GetProperty("format").GetProperty("strict").GetBoolean());}
        await provider.AnalyzeAsync(client,report,"felt tired",false,default);Assert.Equal(1,handler.Calls);
        await provider.AnalyzeAsync(Guid.NewGuid(),report,"felt tired",false,default);Assert.Equal(2,handler.Calls);
        await provider.AnalyzeAsync(client,report,"felt tired",true,default);Assert.Equal(3,handler.Calls);Assert.Contains("private-recovery-note",handler.Sent!);
    }
}
