using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.ViewModels;

namespace WebRecomp.Web.Controllers;

[Authorize(Roles = "Administrator")]
[Route("admin/giveaway-codes")]
public sealed class AdminGiveawayCodesController(
    RecompDbContext db,
    AppUserContextService appUserContext) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(
        CancellationToken cancellationToken)
    {
        var model = await BuildViewModelAsync(cancellationToken);
        return View(model);
    }

    [HttpPost("generate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Generate(
        [Bind(Prefix = "Generate")]
        AdminGiveawayCodeGenerateInput input,
        CancellationToken cancellationToken)
    {
        if (input.RedeemByDate.HasValue
            && input.RedeemByDate.Value < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            ModelState.AddModelError(
                "Generate.RedeemByDate",
                "Redeem-by date cannot be in the past.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildViewModelAsync(cancellationToken);
            invalidModel.Generate = input;
            return View("Index", invalidModel);
        }

        var context = await appUserContext.GetAsync(
            HttpContext,
            cancellationToken);

        var now = DateTime.UtcNow;

        DateTime? redeemByUtc = null;

        if (input.RedeemByDate.HasValue)
        {
            var date = input.RedeemByDate.Value;

            redeemByUtc = new DateTime(
                date.Year,
                date.Month,
                date.Day,
                23,
                59,
                59,
                DateTimeKind.Utc);
        }

        var batch = new GiveawayCodeBatch
        {
            GiveawayCodeBatchId = Guid.NewGuid(),
            BatchName = Clean(input.BatchName),
            Quantity = input.Quantity,
            AccessDurationDays = input.AccessDurationDays,
            RedeemByUtc = redeemByUtc,
            CreatedByUserId = context.User.UserId,
            Notes = Clean(input.Notes),
            CreatedAtUtc = now
        };

        var existingCodes = (await db.GiveawayCodes
                .AsNoTracking()
                .Select(x => x.Code)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var generatedCodes = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        while (generatedCodes.Count < input.Quantity)
        {
            var candidate = CreateGiveawayCode();

            if (!existingCodes.Contains(candidate))
            {
                generatedCodes.Add(candidate);
            }
        }

        var codes = generatedCodes
            .Select(code => new GiveawayCode
            {
                GiveawayCodeId = Guid.NewGuid(),
                GiveawayCodeBatchId = batch.GiveawayCodeBatchId,
                Code = code,
                StatusCode = "AVAILABLE",
                AccessDurationDays = input.AccessDurationDays,
                RedeemByUtc = redeemByUtc,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            })
            .ToList();

        db.GiveawayCodeBatches.Add(batch);
        db.GiveawayCodes.AddRange(codes);

        await db.SaveChangesAsync(cancellationToken);

        TempData["GiveawaySuccess"] =
            $"{codes.Count} giveaway code" +
            (codes.Count == 1 ? " was" : "s were") +
            " generated successfully.";

        return RedirectToAction(nameof(Index));
    }

    [HttpPost("generate-promo")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GeneratePromo(
        [Bind(Prefix = "PromoGenerate")]
        AdminPromoCodeGenerateInput input,
        CancellationToken cancellationToken)
    {
        if (input.RedeemByDate.HasValue
            && input.RedeemByDate.Value < DateOnly.FromDateTime(DateTime.UtcNow))
        {
            ModelState.AddModelError(
                "PromoGenerate.RedeemByDate",
                "Redeem-by date cannot be in the past.");
        }

        var normalizedCode = input.Code
            .Trim()
            .ToUpperInvariant();

        if (await db.PromoCodes.AnyAsync(
                x => x.Code == normalizedCode,
                cancellationToken))
        {
            ModelState.AddModelError(
                "PromoGenerate.Code",
                "That promotion code already exists.");
        }

        if (!ModelState.IsValid)
        {
            var invalidModel = await BuildViewModelAsync(cancellationToken);
            invalidModel.PromoGenerate = input;
            return View("Index", invalidModel);
        }

        var context = await appUserContext.GetAsync(
            HttpContext,
            cancellationToken);

        var now = DateTime.UtcNow;

        DateTime? redeemByUtc = null;

        if (input.RedeemByDate.HasValue)
        {
            var date = input.RedeemByDate.Value;

            redeemByUtc = new DateTime(
                date.Year,
                date.Month,
                date.Day,
                23,
                59,
                59,
                DateTimeKind.Utc);
        }

        var promo = new PromoCode
        {
            PromoCodeId = Guid.NewGuid(),
            PromotionName = input.PromotionName.Trim(),
            Code = normalizedCode,
            StatusCode = "ACTIVE",
            AccessDurationDays = input.AccessDurationDays,
            MaxRedemptions = input.MaxRedemptions,
            RedemptionCount = 0,
            RedeemByUtc = redeemByUtc,
            CreatedByUserId = context.User.UserId,
            Notes = Clean(input.Notes),
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        db.PromoCodes.Add(promo);
        await db.SaveChangesAsync(cancellationToken);

        TempData["GiveawaySuccess"] =
            $"Promotion code {promo.Code} was created successfully.";

        return RedirectToAction(nameof(Index));
    }
    [HttpPost("giveaway/{id:guid}/revoke")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RevokeGiveaway(
        Guid id,
        CancellationToken cancellationToken)
    {
        var code = await db.GiveawayCodes
            .SingleOrDefaultAsync(
                x => x.GiveawayCodeId == id,
                cancellationToken);

        if (code is null)
            return NotFound();

        var now = DateTime.UtcNow;

        if (code.StatusCode != "AVAILABLE")
        {
            TempData["GiveawayError"] =
                $"Code {code.Code} cannot be revoked because its status is {code.StatusCode}.";

            return RedirectToAction(nameof(Index));
        }

        if (code.RedeemByUtc.HasValue
            && code.RedeemByUtc.Value < now)
        {
            code.StatusCode = "EXPIRED";
            code.UpdatedAtUtc = now;

            await db.SaveChangesAsync(cancellationToken);

            TempData["GiveawayError"] =
                $"Code {code.Code} has already expired and was not revoked.";

            return RedirectToAction(nameof(Index));
        }

        var context = await appUserContext.GetAsync(
            HttpContext,
            cancellationToken);

        code.StatusCode = "REVOKED";
        code.RevokedAtUtc = now;
        code.RevokedByUserId = context.User.UserId;
        code.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);

        TempData["GiveawaySuccess"] =
            $"Giveaway code {code.Code} was revoked.";

        return RedirectToAction(nameof(Index));
    }


    [HttpPost("promo/{id:guid}/disable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DisablePromo(
        Guid id,
        CancellationToken cancellationToken)
    {
        var promo = await db.PromoCodes
            .SingleOrDefaultAsync(
                x => x.PromoCodeId == id,
                cancellationToken);

        if (promo is null)
            return NotFound();

        var now = DateTime.UtcNow;

        if (promo.StatusCode != "ACTIVE")
        {
            TempData["GiveawayError"] =
                $"Promotion {promo.Code} cannot be disabled because its status is {promo.StatusCode}.";

            return RedirectToAction(nameof(Index));
        }

        if (promo.RedeemByUtc.HasValue
            && promo.RedeemByUtc.Value < now)
        {
            promo.StatusCode = "EXPIRED";
            promo.UpdatedAtUtc = now;

            await db.SaveChangesAsync(cancellationToken);

            TempData["GiveawayError"] =
                $"Promotion {promo.Code} has already expired.";

            return RedirectToAction(nameof(Index));
        }

        if (promo.RedemptionCount >= promo.MaxRedemptions)
        {
            promo.StatusCode = "EXHAUSTED";
            promo.UpdatedAtUtc = now;

            await db.SaveChangesAsync(cancellationToken);

            TempData["GiveawayError"] =
                $"Promotion {promo.Code} has already reached its redemption limit.";

            return RedirectToAction(nameof(Index));
        }

        promo.StatusCode = "DISABLED";
        promo.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);

        TempData["GiveawaySuccess"] =
            $"Promotion {promo.Code} was disabled.";

        return RedirectToAction(nameof(Index));
    }


    [HttpPost("promo/{id:guid}/enable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnablePromo(
        Guid id,
        CancellationToken cancellationToken)
    {
        var promo = await db.PromoCodes
            .SingleOrDefaultAsync(
                x => x.PromoCodeId == id,
                cancellationToken);

        if (promo is null)
            return NotFound();

        var now = DateTime.UtcNow;

        if (promo.StatusCode != "DISABLED")
        {
            TempData["GiveawayError"] =
                $"Promotion {promo.Code} cannot be enabled because its status is {promo.StatusCode}.";

            return RedirectToAction(nameof(Index));
        }

        if (promo.RedeemByUtc.HasValue
            && promo.RedeemByUtc.Value < now)
        {
            promo.StatusCode = "EXPIRED";
            promo.UpdatedAtUtc = now;

            await db.SaveChangesAsync(cancellationToken);

            TempData["GiveawayError"] =
                $"Promotion {promo.Code} cannot be re-enabled because it has expired.";

            return RedirectToAction(nameof(Index));
        }

        if (promo.RedemptionCount >= promo.MaxRedemptions)
        {
            promo.StatusCode = "EXHAUSTED";
            promo.UpdatedAtUtc = now;

            await db.SaveChangesAsync(cancellationToken);

            TempData["GiveawayError"] =
                $"Promotion {promo.Code} cannot be re-enabled because its redemption limit has been reached.";

            return RedirectToAction(nameof(Index));
        }

        promo.StatusCode = "ACTIVE";
        promo.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);

        TempData["GiveawaySuccess"] =
            $"Promotion {promo.Code} was enabled.";

        return RedirectToAction(nameof(Index));
    }

    [HttpGet("promo/{id:guid}")]
    public async Task<IActionResult> PromoDetail(
        Guid id,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var promo = await db.PromoCodes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.PromoCodeId == id,
                cancellationToken);

        if (promo is null)
            return NotFound();

        var effectiveStatus =
            promo.StatusCode == "ACTIVE"
            && promo.RedeemByUtc.HasValue
            && promo.RedeemByUtc.Value < now
                ? "EXPIRED"
                : promo.RedemptionCount >= promo.MaxRedemptions
                    ? "EXHAUSTED"
                    : promo.StatusCode;

        var redemptionData = await (
            from redemption in db.PromoCodeRedemptions.AsNoTracking()

            join user in db.UserAccounts.AsNoTracking()
                on redemption.UserId equals user.UserId

            join entitlement in db.UserEntitlements.AsNoTracking()
                on redemption.UserEntitlementId
                equals entitlement.UserEntitlementId

            where redemption.PromoCodeId == id

            orderby redemption.RedeemedAtUtc descending

            select new
            {
                UserEmail = user.EmailNormalized,
                redemption.RedeemedAtUtc,
                entitlement.StartsAtUtc,
                entitlement.EndsAtUtc,
                entitlement.RevokedAtUtc
            })
            .ToListAsync(cancellationToken);

        var redemptionRows = redemptionData
            .Select(x => new AdminPromoRedemptionRow
            {
                UserEmail = x.UserEmail,
                RedeemedAtUtc = x.RedeemedAtUtc,
                StartsAtUtc = x.StartsAtUtc,
                EndsAtUtc = x.EndsAtUtc,
                RevokedAtUtc = x.RevokedAtUtc,

                AccessStatus =
                    x.RevokedAtUtc.HasValue
                        ? "REVOKED"
                        : x.StartsAtUtc > now
                            ? "SCHEDULED"
                            : x.EndsAtUtc.HasValue
                              && x.EndsAtUtc.Value <= now
                                ? "EXPIRED"
                                : "ACTIVE"
            })
            .ToList();

        var model = new AdminPromoCodeDetailViewModel
        {
            PromoCodeId = promo.PromoCodeId,
            PromotionName = promo.PromotionName,
            Code = promo.Code,
            StatusCode = effectiveStatus,

            AccessDurationDays = promo.AccessDurationDays,
            MaxRedemptions = promo.MaxRedemptions,
            RedemptionCount = promo.RedemptionCount,

            RedeemByUtc = promo.RedeemByUtc,
            Notes = promo.Notes,
            CreatedAtUtc = promo.CreatedAtUtc,

            Redemptions = redemptionRows
        };

        return View(model);
    }

    private async Task<AdminGiveawayCodesViewModel> BuildViewModelAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var data = await (
            from code in db.GiveawayCodes.AsNoTracking()
            join batch in db.GiveawayCodeBatches.AsNoTracking()
                on code.GiveawayCodeBatchId
                equals batch.GiveawayCodeBatchId
            join redeemedUser in db.UserAccounts.AsNoTracking()
                on code.RedeemedByUserId
                equals (Guid?)redeemedUser.UserId
                into redeemedUsers
            from redeemedUser in redeemedUsers.DefaultIfEmpty()
            orderby code.CreatedAtUtc descending
            select new
            {
                code.GiveawayCodeId,
                code.GiveawayCodeBatchId,
                code.Code,
                code.StatusCode,
                batch.BatchName,
                code.AccessDurationDays,
                code.RedeemByUtc,
                RedeemedByEmail = redeemedUser == null
                    ? null
                    : redeemedUser.EmailNormalized,
                code.RedeemedAtUtc,
                code.AccessExpiresAtUtc,
                code.CreatedAtUtc
            })
            .Take(250)
            .ToListAsync(cancellationToken);

        var rows = data
            .Select(x => new AdminGiveawayCodeRow
            {
                GiveawayCodeId = x.GiveawayCodeId,
                GiveawayCodeBatchId = x.GiveawayCodeBatchId,
                Code = x.Code,

                StatusCode =
                    x.StatusCode == "AVAILABLE"
                    && x.RedeemByUtc.HasValue
                    && x.RedeemByUtc.Value < now
                        ? "EXPIRED"
                        : x.StatusCode,

                BatchName = x.BatchName,
                AccessDurationDays = x.AccessDurationDays,
                RedeemByUtc = x.RedeemByUtc,
                RedeemedByEmail = x.RedeemedByEmail,
                RedeemedAtUtc = x.RedeemedAtUtc,
                AccessExpiresAtUtc = x.AccessExpiresAtUtc,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToList();

        var promoData = await db.PromoCodes
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .Select(x => new AdminPromoCodeRow
            {
                PromoCodeId = x.PromoCodeId,
                PromotionName = x.PromotionName,
                Code = x.Code,

                StatusCode =
                    x.StatusCode == "ACTIVE"
                    && x.RedeemByUtc.HasValue
                    && x.RedeemByUtc.Value < now
                        ? "EXPIRED"
                        : x.RedemptionCount >= x.MaxRedemptions
                            ? "EXHAUSTED"
                            : x.StatusCode,

                AccessDurationDays = x.AccessDurationDays,
                MaxRedemptions = x.MaxRedemptions,
                RedemptionCount = x.RedemptionCount,
                RedeemByUtc = x.RedeemByUtc,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);

        return new AdminGiveawayCodesViewModel
        {
            Codes = rows,
            PromoCodes = promoData
        };
    }

    private static string CreateGiveawayCode()
    {
        return RandomNumberGenerator
            .GetInt32(0, 1_000_000)
            .ToString("D6");
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }
}
