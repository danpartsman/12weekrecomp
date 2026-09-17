using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Controllers;

[ApiController]
[Route("api/system")]
public sealed class SystemController(
    RecompDbContext db,
    IConfiguration configuration) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("status")]
    public IActionResult Status() => Ok(new
    {
        application = "Web Recomp",
        specification =
            configuration["WebRecomp:SpecificationVersion"] ?? "1005a",
        engine =
            configuration["WebRecomp:EngineVersion"] ?? "1001.0",
        status = "running"
    });

    [HttpGet("database")]
    public async Task<IActionResult> Database(
        CancellationToken cancellationToken)
    {
        var connected =
            await db.Database.CanConnectAsync(cancellationToken);

        if (!connected)
            return Problem("Database connection failed.");

        var config = await db.ConfigVersions
            .Where(x => x.StatusCode == "PUBLISHED")
            .OrderByDescending(x => x.EffectiveFromUtc)
            .Select(x => x.VersionCode)
            .FirstOrDefaultAsync(cancellationToken);

        return Ok(new
        {
            database = "connected",
            activeConfiguration = config
        });
    }
}