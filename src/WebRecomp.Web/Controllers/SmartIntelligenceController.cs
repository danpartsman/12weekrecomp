using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.SmartIntelligence;
namespace WebRecomp.Web.Controllers;

[Authorize, Route("SmartIntelligence")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class SmartIntelligenceController(AppUserContextService users,IntelligenceDataSource data,
    IntelligenceConnection connection,IEnumerable<ISmartAnalysisProvider> providers) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(int days=42,string sensitivity="balanced",CancellationToken ct=default)
    {
        var context=await users.GetAsync(HttpContext,ct);
        if(context.Client is null||context.Program is null)return RedirectToAction("Index","Intake");
        var report=await data.LoadAsync(context.Client.ClientId,AppUserContextService.GetClientToday(context.Client),days,sensitivity,ct);
        return View(new IntelligencePage(report,connection.Ready,connection.Model,User.IsInRole("Administrator")));
    }
    [HttpPost("Analyze"),ValidateAntiForgeryToken,EnableRateLimiting("smart-analysis"),RequestSizeLimit(8192)]
    public async Task<IActionResult> Analyze([FromBody] AnalysisInput input,CancellationToken ct)
    {
        if(!ModelState.IsValid)return BadRequest(new {message="Check the analysis options and keep context under 1,500 characters."});
        var context=await users.GetAsync(HttpContext,ct);
        if(context.Client is null||context.Program is null)return Forbid();
        var provider=providers.SingleOrDefault(x=>x.Id==input.Provider);
        if(provider is null)return BadRequest(new {message="This analysis provider is not available."});
        if(!provider.Ready)return StatusCode(503,new {message="OpenAI needs an API connection. An administrator can connect it on this page."});
        var report=await data.LoadAsync(context.Client.ClientId,AppUserContextService.GetClientToday(context.Client),input.Days,input.Sensitivity,ct);
        try{return Json(await provider.AnalyzeAsync(context.Client.ClientId,report,input.Context??"",input.IncludeNotes,ct));}
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){return StatusCode(504,new {message="The analysis timed out. Your trends remain available; try the analysis again."});}
        catch(Exception ex) when(ex is HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
        {return StatusCode(502,new {message=ex is HttpRequestException or InvalidOperationException?ex.Message:"OpenAI returned an incomplete analysis. Please retry."});}
    }
    [Authorize(Roles="Administrator"),HttpPost("Connection"),ValidateAntiForgeryToken,EnableRateLimiting("smart-analysis"),RequestSizeLimit(4096)]
    public IActionResult Connection([FromForm] ConnectionInput input)
    {
        if(!ModelState.IsValid){TempData["SmartConnection"]="Enter a valid API key and model.";return RedirectToAction(nameof(Index));}
        try{connection.Save(input.ApiKey,input.Model);TempData["SmartConnection"]="OpenAI connection saved. Run an analysis to verify access.";}
        catch(ArgumentException ex){TempData["SmartConnection"]=ex.Message;}
        return RedirectToAction(nameof(Index));
    }
    public sealed class AnalysisInput
    {
        [Range(42,168)]public int Days {get;set;}=42;
        [RegularExpression("^(balanced|steady|responsive)$")]public string Sensitivity {get;set;}="balanced";
        [Required,StringLength(40)]public string Provider {get;set;}="openai";
        [StringLength(1500)]public string? Context {get;set;}
        public bool IncludeNotes {get;set;}=true;
    }
    public sealed class ConnectionInput
    {
        [Required,StringLength(500)]public string ApiKey {get;set;}="";
        [Required,StringLength(100)]public string Model {get;set;}="gpt-5-mini";
    }
}
