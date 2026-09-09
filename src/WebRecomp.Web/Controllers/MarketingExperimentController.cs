using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;

namespace WebRecomp.Web.Controllers;

[ApiController]
[AllowAnonymous]
[Route("marketing")]
public sealed class MarketingExperimentController(RecompDbContext db) : ControllerBase
{
    private static readonly HashSet<string> AllowedVariants =
        new(StringComparer.OrdinalIgnoreCase) { "A", "B", "C", "D", "U" };

    private static readonly HashSet<string> AllowedEvents =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "WELCOME_VIEW",
            "CLICK_START_FREE",
            "CLICK_EVIDENCE",
            "EVIDENCE_VIEW",
            "EVIDENCE_START_FREE"
        };

    [HttpPost("track")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Track(
        [FromBody] MarketingTrackInput? input,
        CancellationToken cancellationToken)
    {
        if (input is null)
            return BadRequest();

        var sessionId = (input.SessionId ?? "").Trim();
        var variant = (input.Variant ?? "U").Trim().ToUpperInvariant();
        var eventType = (input.EventType ?? "").Trim().ToUpperInvariant();
        var pagePath = (input.PagePath ?? "").Trim();

        if (sessionId.Length < 16 || sessionId.Length > 64)
            return BadRequest();

        if (!AllowedVariants.Contains(variant))
            variant = "U";

        if (!AllowedEvents.Contains(eventType))
            return BadRequest();

        if (pagePath.Length == 0)
            pagePath = "/";

        if (pagePath.Length > 200)
            pagePath = pagePath[..200];

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO [app].[MarketingExperimentEvents]
                ([anonymous_session_id], [variant_code], [event_type], [page_path], [created_at_utc])
            VALUES
                ({sessionId}, {variant}, {eventType}, {pagePath}, SYSUTCDATETIME());
            """,
            cancellationToken);

        return NoContent();
    }
}

public sealed class MarketingTrackInput
{
    public string? SessionId { get; set; }
    public string? Variant { get; set; }
    public string? EventType { get; set; }
    public string? PagePath { get; set; }
}
