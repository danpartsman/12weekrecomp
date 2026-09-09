using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace WebRecomp.Web.Controllers;

[Authorize]
[Route("role-diagnostic")]
public sealed class RoleDiagnosticController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        var roles = User.Claims
            .Where(x =>
                x.Type == ClaimTypes.Role ||
                x.Type == "roles" ||
                x.Type.EndsWith("/role", StringComparison.OrdinalIgnoreCase))
            .Select(x => new
            {
                x.Type,
                x.Value
            })
            .ToList();

        return Json(new
        {
            authenticated = User.Identity?.IsAuthenticated == true,
            name = User.Identity?.Name,
            isAdministrator = User.IsInRole("Administrator"),
            isCoach = User.IsInRole("Coach"),
            roles
        });
    }

    [Authorize(Roles = "Administrator")]
    [HttpGet("admin")]
    public IActionResult AdministratorOnly()
    {
        return Json(new
        {
            success = true,
            message = "Administrator authorization succeeded.",
            administrator = User.IsInRole("Administrator")
        });
    }

    [Authorize(Roles = "Coach")]
    [HttpGet("coach")]
    public IActionResult CoachOnly()
    {
        return Json(new
        {
            success = true,
            message = "Coach authorization succeeded.",
            coach = User.IsInRole("Coach")
        });
    }
}
