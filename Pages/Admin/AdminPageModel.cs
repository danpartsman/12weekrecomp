using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TwelveWeekRecomp.Live.Services;

namespace TwelveWeekRecomp.Live.Pages.Admin;

public abstract class AdminPageModel : PageModel
{
    private readonly AdminAccessService _adminAccess;

    protected AdminPageModel(AdminAccessService adminAccess)
    {
        _adminAccess = adminAccess;
    }

    public AdminAccessResult? AdminIdentity { get; private set; }

    protected IActionResult? RequireAdmin(string returnPath = "/admin")
    {
        var directBase = _adminAccess.DirectAdminBaseUrl;
        var directHost = new Uri(directBase).Host;
        if (!string.Equals(Request.Host.Host, directHost, StringComparison.OrdinalIgnoreCase))
        {
            var target = directBase + returnPath;
            if (Request.QueryString.HasValue && !target.Contains('?'))
                target += Request.QueryString.Value;
            return Redirect(target);
        }

        AdminIdentity = _adminAccess.Evaluate(HttpContext);

        if (!AdminIdentity.IsAuthenticated)
        {
            var redirect = Uri.EscapeDataString(returnPath);
            return Redirect($"/.auth/login/aad?post_login_redirect_uri={redirect}");
        }

        if (!AdminIdentity.IsAuthorized)
            return StatusCode(StatusCodes.Status403Forbidden);

        return null;
    }
}
