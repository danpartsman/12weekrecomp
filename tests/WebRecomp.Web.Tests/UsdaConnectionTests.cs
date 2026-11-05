using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using WebRecomp.Web.Services.Nutrition;
namespace WebRecomp.Web.Tests;
public sealed class UsdaConnectionTests
{
    private sealed class Host:IWebHostEnvironment
    {
        public string ApplicationName{get;set;}="test";public string EnvironmentName{get;set;}="test";
        public string ContentRootPath{get;set;}=Path.GetTempPath();public string WebRootPath{get;set;}=Path.GetTempPath();
        public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
    }
    [Fact]
    public void Saved_key_is_encrypted_persistent_and_overrides_environment_without_restart()
    {
        var folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try
        {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Nutrition:CredentialsPath",folder},{"USDA_FDC_API_KEY","environment-test-value"}}).Build();
            var protector=new EphemeralDataProtectionProvider();var connection=new UsdaConnection(config,new Host(),protector);
            Assert.Equal("environment-test-value",connection.ApiKey);Assert.Equal("Azure/environment setting",connection.Source);
            connection.Save("saved-test-value");
            Assert.DoesNotContain("saved-test-value",File.ReadAllText(Path.Combine(folder,"usda.protected")));
            Assert.Equal("saved-test-value",connection.ApiKey);Assert.True(connection.Ready);
            Assert.Equal("saved-test-value",new UsdaConnection(config,new Host(),protector).ApiKey);
            connection.Save("replacement-test-value");Assert.Equal("replacement-test-value",connection.ApiKey);
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
    [Theory]
    [InlineData("")][InlineData("short")][InlineData("bad key with spaces")][InlineData("<script>alert(1)</script>")]
    public void Invalid_input_does_not_replace_a_saved_key(string invalid)
    {
        var folder=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));
        try
        {
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"Nutrition:CredentialsPath",folder}}).Build();
            var connection=new UsdaConnection(config,new Host(),new EphemeralDataProtectionProvider());connection.Save("existing-test-key");
            Assert.Throws<ArgumentException>(()=>connection.Save(invalid));Assert.Equal("existing-test-key",connection.ApiKey);
        }
        finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
    }
}
