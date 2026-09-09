using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/marketing")]
public sealed class AdminMarketingController(RecompDbContext db) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        const string sql = """
        WITH variants AS
        (
            SELECT 'U' AS variant_code
            UNION ALL SELECT 'A'
            UNION ALL SELECT 'B'
            UNION ALL SELECT 'C'
            UNION ALL SELECT 'D'
        )
        SELECT
            v.variant_code,
            COUNT(DISTINCT CASE WHEN e.event_type = 'WELCOME_VIEW'
                                THEN e.anonymous_session_id END) AS welcome_visitors,
            COUNT(DISTINCT CASE WHEN e.event_type = 'CLICK_START_FREE'
                                THEN e.anonymous_session_id END) AS start_free_visitors,
            COUNT(DISTINCT CASE WHEN e.event_type = 'CLICK_EVIDENCE'
                                THEN e.anonymous_session_id END) AS evidence_click_visitors,
            COUNT(DISTINCT CASE WHEN e.event_type = 'EVIDENCE_VIEW'
                                THEN e.anonymous_session_id END) AS evidence_visitors,
            COUNT(DISTINCT CASE WHEN e.event_type = 'EVIDENCE_START_FREE'
                                THEN e.anonymous_session_id END) AS evidence_start_visitors
        FROM variants v
        LEFT JOIN app.MarketingExperimentEvents e
            ON e.variant_code = v.variant_code
        GROUP BY v.variant_code
        ORDER BY
            CASE v.variant_code
                WHEN 'U' THEN 0
                WHEN 'A' THEN 1
                WHEN 'B' THEN 2
                WHEN 'C' THEN 3
                WHEN 'D' THEN 4
                ELSE 5
            END;
        """;

        var rows =
            new List<MarketingExperimentVariantRow>();

        var connection =
            db.Database.GetDbConnection();

        var shouldClose =
            connection.State
            != System.Data.ConnectionState.Open;

        if (shouldClose)
        {
            await connection.OpenAsync(
                cancellationToken);
        }

        try
        {
            await using var command =
                connection.CreateCommand();

            command.CommandText =
                sql;

            await using var reader =
                await command.ExecuteReaderAsync(
                    cancellationToken);

            while (
                await reader.ReadAsync(
                    cancellationToken))
            {
                rows.Add(
                    new MarketingExperimentVariantRow
                    {
                        VariantCode =
                            reader.GetString(0),

                        WelcomeVisitors =
                            Convert.ToInt32(
                                reader.GetValue(1)),

                        StartFreeVisitors =
                            Convert.ToInt32(
                                reader.GetValue(2)),

                        EvidenceClickVisitors =
                            Convert.ToInt32(
                                reader.GetValue(3)),

                        EvidenceVisitors =
                            Convert.ToInt32(
                                reader.GetValue(4)),

                        EvidenceStartVisitors =
                            Convert.ToInt32(
                                reader.GetValue(5))
                    });
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }

        return View(
            new MarketingExperimentDashboardViewModel
            {
                Variants = rows,
                GeneratedAtUtc = DateTime.UtcNow
            });
    }
}
