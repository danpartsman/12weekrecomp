using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.ViewComponents;

public sealed class FreeUseOverlayViewComponent(RecompDbContext db) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        var setting = await db.GlobalSettings.AsNoTracking()
            .Where(x => x.SettingCode == "PUBLIC_FREE_OVERLAY_ENABLED")
            .Select(x => x.DecimalValue)
            .FirstOrDefaultAsync();
        return setting == 0m ? Content(string.Empty) : View();
    }
}
