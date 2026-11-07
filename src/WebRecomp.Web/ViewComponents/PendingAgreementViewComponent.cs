using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Data;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Notifications;

namespace WebRecomp.Web.ViewComponents;
public sealed class PendingAgreementViewComponent(AppUserContextService users, RecompDbContext db, NotificationFiles files) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (User.Identity?.IsAuthenticated != true) return Content("");
        var context = await users.GetAsync(HttpContext, HttpContext.RequestAborted);
        if (context.Client is null) return Content("");
        var agreements = await PendingFinancialAgreements.ReadAsync(db, files, context.Client.ClientId, context.User.UserId, HttpContext.RequestAborted);
        return agreements.Count == 0 ? Content("") : View("~/Views/Shared/Components/PendingAgreement/Default.cshtml", agreements);
    }
}
