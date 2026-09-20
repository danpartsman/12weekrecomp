using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace WebRecomp.Web.Controllers;

[AllowAnonymous]
public sealed class WhyRecompositionMattersController : Controller
{
    [HttpGet("/recomp/why-recomposition-matters")]
    public IActionResult Index()
    {
        ViewData["PublicPage"] = true;
        return View("~/Views/Home/WhyRecompositionMatters.cshtml");
    }
}
