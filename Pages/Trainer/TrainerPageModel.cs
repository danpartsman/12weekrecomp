using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TwelveWeekRecomp.Live.Services;

namespace TwelveWeekRecomp.Live.Pages.Trainer;

public abstract class TrainerPageModel : PageModel
{
    private readonly StaffAccessService _staffAccess;

    protected TrainerPageModel(StaffAccessService staffAccess)
    {
        _staffAccess = staffAccess;
    }

    public StaffAccessResult? StaffIdentity { get; private set; }

    protected IActionResult? RequireTrainer(string returnPath = "/trainer")
    {
        var directBase = _staffAccess.DirectBaseUrl;
        var directHost = new Uri(directBase).Host;

        if (!string.Equals(Request.Host.Host, directHost, StringComparison.OrdinalIgnoreCase))
        {
            var target = directBase + returnPath;
            if (Request.QueryString.HasValue && !target.Contains('?'))
                target += Request.QueryString.Value;
            return Redirect(target);
        }

        StaffIdentity = _staffAccess.Evaluate(HttpContext);

        if (!StaffIdentity.IsAuthenticated)
        {
            var redirect = Uri.EscapeDataString(returnPath);
            return Redirect($"/.auth/login/aad?post_login_redirect_uri={redirect}");
        }

        // Administrators can enter the trainer workspace for operational support.
        if (!StaffIdentity.IsTrainer && !StaffIdentity.IsAdministrator)
            return StatusCode(StatusCodes.Status403Forbidden);

        return null;
    }
}
