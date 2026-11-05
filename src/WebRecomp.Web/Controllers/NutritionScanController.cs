using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using WebRecomp.NutritionIntelligence.Contracts;
using WebRecomp.NutritionIntelligence.Models;
using WebRecomp.NutritionIntelligence.Persistence;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Nutrition;
namespace WebRecomp.Web.Controllers;
[Authorize,Route("nutrition/scan"),ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class NutritionScanController(AppUserContextService users,INutritionFoodLogStore store,IEnumerable<IFoodCompositionProvider> providers,IHttpClientFactory clients,IMemoryCache cache):Controller
{
    [HttpGet("barcode")]
    public async Task<IActionResult> Barcode(string? code,CancellationToken ct)
    {
        var context=await users.GetAsync(HttpContext,ct);
        if(context.Client is null||context.Program is null)return Unauthorized();
        if(!context.Program.NutritionFoodLogEnabled)return Forbid();
        var normalized=FoodBarcode.Normalize(code);
        if(normalized is null)return BadRequest(new{message="Use a valid UPC or EAN product barcode (8, 12, 13 or 14 digits)."});
        if(cache.TryGetValue<object>("food-barcode:"+normalized,out var cached))return Json(cached);
        var policy=await store.GetProviderPolicyAsync(NutritionProviderCodes.UsdaFoodDataCentral,ct);
        var provider=providers.FirstOrDefault(x=>x.ProviderCode==NutritionProviderCodes.UsdaFoodDataCentral);
        if(policy?.IsEnabled==true&&provider is not null)
        {
            try
            {
                var queries=new[]{code!.Trim(),normalized.TrimStart('0')}.Distinct().ToArray();
                foreach(var query in queries)
                {
                    var page=await provider.SearchAsync(new FoodSearchRequest(query,1,200,["Branded"]),ct);
                    var food=page.Foods.FirstOrDefault(x=>FoodBarcode.Normalize(x.GtinUpc)==normalized);
                    if(food is null)continue;
                    var result=new{success=true,providerCode=food.ProviderCode,sourceFoodId=food.SourceFoodId,description=food.Description,brandName=food.BrandOwner,source="USDA FoodData Central"};
                    cache.Set("food-barcode:"+normalized,(object)result,TimeSpan.FromHours(6));return Json(result);
                }
            }
            catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
            catch{/* The free barcode fallback works even if USDA is not configured. */}
        }
        try
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var client=clients.CreateClient();
            using var request=new HttpRequestMessage(HttpMethod.Get,"https://world.openfoodfacts.org/api/v3/product/"+code!.Trim()+"?fields=code,product_name,brands,nutriments,nutrition_data_per");
            request.Headers.UserAgent.ParseAdd("12WeekRecomp/1.0 (+https://12weekrecomp.com/contact)");
            using var response=await client.SendAsync(request,timeout.Token);
            if(response.StatusCode==System.Net.HttpStatusCode.NotFound)return NotFound(new{message="No exact product was found. Scan the nutrition label or use food search."});
            if(!response.IsSuccessStatusCode)return StatusCode(503,new{message="Free barcode lookup is temporarily unavailable. Scan the nutrition label instead."});
            using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            var root=json.RootElement;
            if(!root.TryGetProperty("product",out var product)||!product.TryGetProperty("code",out var found)||FoodBarcode.Normalize(found.GetString())!=normalized)
                return NotFound(new{message="No exact product was found. Scan the nutrition label or use food search."});
            product.TryGetProperty("nutriments",out var nutrients);
            decimal? Value(string name)=>nutrients.ValueKind==JsonValueKind.Object&&nutrients.TryGetProperty(name,out var item)&&item.ValueKind==JsonValueKind.Number&&item.TryGetDecimal(out var value)&&value>=0?value:null;
            string Text(string name)=>product.TryGetProperty(name,out var item)&&item.ValueKind==JsonValueKind.String?item.GetString()??"":"";
            var resultOff=new{success=true,source="Open Food Facts",sourceUrl="https://world.openfoodfacts.org/product/"+code.Trim(),scanSource="OPEN_FOOD_FACTS",scanBarcode=code.Trim(),description=Text("product_name"),brandName=Text("brands"),servingSize=100,servingUnitCode="g",energyKcal=Value("energy-kcal_100g"),proteinG=Value("proteins_100g"),carbohydrateG=Value("carbohydrates_100g"),fatG=Value("fat_100g"),fiberG=Value("fiber_100g")};
            cache.Set("food-barcode:"+normalized,(object)resultOff,TimeSpan.FromHours(6));return Json(resultOff);
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
        catch{return StatusCode(503,new{message="Free barcode lookup is unavailable right now. You can still scan the nutrition label."});}
    }
}
