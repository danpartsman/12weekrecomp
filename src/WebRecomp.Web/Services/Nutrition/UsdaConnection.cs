using Microsoft.AspNetCore.DataProtection;
namespace WebRecomp.Web.Services.Nutrition;

public sealed class UsdaConnection(IConfiguration config,IWebHostEnvironment environment,IDataProtectionProvider protection)
{
    private readonly IDataProtector protector=protection.CreateProtector("12WR.Nutrition.USDA.Connection.v1");
    private readonly object gate=new();
    private string PathName=>Path.Combine(config["Nutrition:CredentialsPath"]??Path.Combine(Environment.GetEnvironmentVariable("HOME")??environment.ContentRootPath,"data","nutrition-credentials"),"usda.protected");
    private string? SavedKey()
    {
        lock(gate)
        {
            try{return File.Exists(PathName)?protector.Unprotect(File.ReadAllText(PathName)):null;}
            catch(Exception ex) when(ex is IOException or System.Security.Cryptography.CryptographicException){return null;}
        }
    }
    public string? ApiKey=>SavedKey()??config["USDA_FDC_API_KEY"];
    public bool Ready=>!string.IsNullOrWhiteSpace(ApiKey);
    public string Source=>!string.IsNullOrWhiteSpace(SavedKey())?"Saved administration key":Ready?"Azure/environment setting":"Not configured";
    public void Save(string? key)
    {
        var value=key?.Trim();
        if(string.IsNullOrEmpty(value)||value.Length<8||value.Length>128||value.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_'&&c!='-'))
            throw new ArgumentException("Enter a valid USDA API key (8–128 letters, numbers, underscores or hyphens).");
        lock(gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            var temporary=PathName+".tmp";
            File.WriteAllText(temporary,protector.Protect(value));
            File.Move(temporary,PathName,true);
        }
    }
}
