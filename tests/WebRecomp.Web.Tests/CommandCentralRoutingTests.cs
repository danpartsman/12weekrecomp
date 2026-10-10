using Microsoft.AspNetCore.Mvc;
using WebRecomp.Web.Controllers;

namespace WebRecomp.Web.Tests;

public sealed class CommandCentralRoutingTests
{
    [Fact]
    public void LegacyBookmarks_PreserveSelectedDayAndMealPlan()
    {
        var controller = new DashboardController(null!, null!, null!);
        var result = Assert.IsType<RedirectToActionResult>(controller.LegacyDashboard(new DateOnly(2026, 10, 9), 3));
        Assert.Equal("CommandCentral", result.ActionName);
        Assert.Equal("2026-10-09", result.RouteValues!["date"]);
        Assert.Equal(3, result.RouteValues["occasions"]);
    }

    [Fact]
    public void InvalidLegacyDate_DoesNotSilentlyDisplayToday()
    {
        var controller = new DashboardController(null!, null!, null!);
        controller.ModelState.AddModelError("date", "Invalid date");
        Assert.IsType<BadRequestObjectResult>(controller.LegacyDashboard(null, null));
    }
}
