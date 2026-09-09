using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Coach")]
[Route("coach")]
public sealed class CoachController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        return View();
    }
}
