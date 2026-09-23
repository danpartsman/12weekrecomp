using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebRecomp.Web.Controllers;

[AllowAnonymous]
public sealed class HowItWorksController : Controller
{
    [HttpGet("/recomp/how-it-works")]
    public IActionResult Index()
    {
        ViewData["PublicPage"] = true;
        return View("~/Views/Home/HowItWorks.cshtml");
    }
}
