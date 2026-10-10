using System.Net;
using System.Text;
using System.Text.Json;
using WebRecomp.Web.Services.Fitdays;

namespace WebRecomp.Web.Tests;

public sealed class FitdaysPreviewTests
{
    [Fact]
    public void SignatureMatchesPublishedProtocolVector()
    {
        var p = new Dictionary<string,string> { ["app_ver"]="1.21.0", ["client_id"]="DC156F861A09E36508B5AF9EBBFD49F1", ["country"]="US", ["device_model"]="AndroidSDKbuiltforarm64-6.0", ["language"]="en", ["os_type"]="0", ["request_id"]="1fbbaba4e2de090c8a39df5c0a0f8163", ["source"]="0", ["timestamp"]="1777683482", ["token"]="", ["uid"]="0" };
        Assert.Equal("d201da49271424d1593163b9355f99bc", FitdaysPreviewClient.Sign(p));
    }
    [Fact]
    public void ParseSeparatesProfilesSkipsDeletedAndConvertsLocalDate()
    {
        using var doc = JsonDocument.Parse("""
        {"account":{"users":[{"suid":1,"nickname":"One"},{"suid":2,"nickname":"Two"}]},"weight_list":[
        {"suid":1,"measured_time":1791675000,"weight_g":100000,"weight_kg":101,"weight_lb":999,"data_id":"a","bfr":25},
        {"suid":2,"measured_time":1791675000,"weight_kg":60,"data_id":"b"},
        {"suid":1,"measured_time":1791675000,"weight_kg":99,"is_deleted":1},
        {"suid":3,"measured_time":1791675000,"weight_kg":99} ]}
        """);
        var result = FitdaysPreviewClient.Parse(doc.RootElement,"America/Los_Angeles");
        Assert.Equal(2,result.People.Count);
        Assert.Equal(2,result.Readings.Count);
        var first = result.Readings.Single(r=>r.PersonId==1);
        Assert.Equal(220.462262185m, first.WeightLb);
        Assert.Equal(TimeSpan.FromHours(-7), first.MeasuredAt.Offset);
        Assert.Equal(25m,first.Bfr);
        Assert.Equal("a",first.RecordId);
    }
    [Fact]
    public void ParseHandlesMissingListsAndRejectsInvalidWeights()
    {
        using var empty=JsonDocument.Parse("{}");
        Assert.Empty(FitdaysPreviewClient.Parse(empty.RootElement,"UTC").Readings);
        using var doc=JsonDocument.Parse("""{"users":[{"suid":1}],"weight_list":[{"suid":1,"measured_time":999999999999999,"weight_kg":70},{"suid":1,"measured_time":1,"weight_kg":-3}]}""");
        Assert.Empty(FitdaysPreviewClient.Parse(doc.RootElement,"UTC").Readings);
    }
    [Fact]
    public void InvalidRegionCannotBecomeAnArbitraryUrl() => Assert.Throws<FitdaysPreviewException>(()=>FitdaysPreviewClient.RegionHost("https://example.com"));

    private sealed class Handler(Func<HttpRequestMessage,Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>respond(request);
    }
    private static HttpResponseMessage Json(string json)=>new(HttpStatusCode.OK) { Content=new StringContent(json,Encoding.UTF8,"application/json") };
    [Fact]
    public async Task LoginHashesPasswordAndReadsSession()
    {
        using var http=new HttpClient(new Handler(async request=>
        {
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var hash=body.RootElement.GetProperty("password").GetString();
            Assert.Equal(FitdaysPreviewClient.HashPassword("example-password"),hash);
            Assert.DoesNotContain("example-password",hash!);
            return Json("""{"code":0,"data":{"token":"session-token","account":{"uid":12}}}""");
        }));
        var session=await new FitdaysPreviewClient(http).LoginAsync("example@example.com","example-password","us",default);
        Assert.Equal(12,session.Uid);
        Assert.Equal("session-token",session.Token);
    }
    [Fact]
    public async Task UntrustedRedirectIsRejectedBeforeCredentialsAreForwarded()
    {
        var calls=0;
        using var http=new HttpClient(new Handler(_=> {calls++;return Task.FromResult(Json("""{"code":302,"data":{"domain":"https://example.com"}}"""));}));
        await Assert.ThrowsAsync<FitdaysPreviewException>(()=>new FitdaysPreviewClient(http).LoginAsync("example@example.com","example-password","us",default));
        Assert.Equal(1,calls);
    }
    [Fact]
    public async Task UpstreamErrorDoesNotExposeServerResponse()
    {
        using var http=new HttpClient(new Handler(_=>Task.FromResult(Json("""{"code":1001,"msg":"private provider detail","data":null}"""))));
        var error=await Assert.ThrowsAsync<FitdaysPreviewException>(()=>new FitdaysPreviewClient(http).LoginAsync("example@example.com","example-password","us",default));
        Assert.DoesNotContain("private provider detail",error.Message);
    }
    [Fact]
    public async Task SyncUsesNewestStartAndFiltersOutsideThirtyDays()
    {
        using var http=new HttpClient(new Handler(async request=>
        {
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            var start=body.RootElement.GetProperty("start_time").GetInt64();
            var end=body.RootElement.GetProperty("end_time").GetInt64();
            Assert.Equal(30*86400,start-end);
            return Json("""{"code":0,"data":{"users":[{"suid":1}],"weight_list":[{"suid":1,"measured_time":1,"weight_kg":70}]}}""");
        }));
        var result=await new FitdaysPreviewClient(http).ReadAsync(new FitdaysSession {Host=FitdaysPreviewClient.RegionHost("us"),Token="token",Uid=1},"UTC",default);
        Assert.Empty(result.Readings);
    }
}
