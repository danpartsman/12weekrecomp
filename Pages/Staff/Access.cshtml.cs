using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TwelveWeekRecomp.Live.Services;

namespace TwelveWeekRecomp.Live.Pages.Staff;

public sealed class AccessModel : PageModel
{
    private readonly StaffAccessService _staffAccess;

    public AccessModel(StaffAccessService staffAccess)
    {
        _staffAccess = staffAccess;
    }

    public StaffAccessResult? StaffIdentity { get; private set; }

    public IActionResult OnGet()
    {
        const string returnPath = "/staff/access";
        var directBase = _staffAccess.DirectBaseUrl;
        var directHost = new Uri(directBase).Host;

        if (!string.Equals(Request.Host.Host, directHost, StringComparison.OrdinalIgnoreCase))
            return Redirect(directBase + returnPath);

        StaffIdentity = _staffAccess.Evaluate(HttpContext);

        if (!StaffIdentity.IsAuthenticated)
        {
            var redirect = Uri.EscapeDataString(returnPath);
            return Redirect($"/.auth/login/aad?post_login_redirect_uri={redirect}");
        }

        return Page();
    }
}
