using Microsoft.AspNetCore.DataProtection;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace WebRecomp.Web.Services.SmartIntelligence;

public sealed class IntelligenceConnection(IConfiguration config, IWebHostEnvironment environment, IDataProtectionProvider protection)
{
    private readonly IDataProtector protector=protection.CreateProtector("12WR.SmartIntelligence.Connection.v1");
    private readonly object gate=new();
    private string PathName=>Path.Combine(config["SmartIntelligence:DataPath"]??Path.Combine(Environment.GetEnvironmentVariable("HOME")??environment.ContentRootPath,"data","smart-intelligence"),"openai.protected");
    private sealed record Saved(string Key,string Model);
    private Saved? Read()
    {
        lock(gate) { try { return File.Exists(PathName)?JsonSerializer.Deserialize<Saved>(protector.Unprotect(File.ReadAllText(PathName))):null; }
            catch(Exception ex) when(ex is IOException or System.Security.Cryptography.CryptographicException or JsonException){return null;} }
    }
    public string? ApiKey=>new[]{config["SmartIntelligence:OpenAI:ApiKey"],config["OpenAI:ApiKey"],config["OPENAI_API_KEY"],Read()?.Key}.FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x));
    public string Model=>config["SmartIntelligence:OpenAI:Model"]??Read()?.Model??"gpt-5-mini";
    public bool Ready=>!string.IsNullOrWhiteSpace(ApiKey);
    public void Save(string key,string model)
    {
        if(string.IsNullOrWhiteSpace(key)||key.Length>500||key.Any(char.IsWhiteSpace))throw new ArgumentException("Enter a valid API key in the connection form.");
        if(!Regex.IsMatch(model,@"^[a-zA-Z0-9][a-zA-Z0-9._-]{0,99}$"))throw new ArgumentException("Use a valid OpenAI model identifier.");
        lock(gate) { Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);var temp=PathName+".tmp";
            File.WriteAllText(temp,protector.Protect(JsonSerializer.Serialize(new Saved(key.Trim(),model))));File.Move(temp,PathName,true); }
    }
}
