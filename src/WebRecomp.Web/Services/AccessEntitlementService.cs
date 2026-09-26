using System.Data;
using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;

namespace WebRecomp.Web.Services;

public sealed record AccessRedemptionResult(
    bool Success,
    string Message,
    DateTime? AccessExpiresAtUtc = null);

public sealed record AccessCodeValidationResult(
    bool Success,
    string Message,
    string? NormalizedCode = null,
    string? PromotionName = null,
    int? AccessDurationDays = null);

public sealed class AccessEntitlementService(
    RecompDbContext db,
    IConfiguration configuration)
{
    public bool IsEntitlementRequired =>
        configuration.GetValue<bool>("WebRecomp:RequireEntitlement");

    public async Task<bool> HasApplicationAccessAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        // New entitlement architecture.
        if (await db.UserEntitlements
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.UserId == userId
                    && x.RevokedAtUtc == null
                    && x.StartsAtUtc <= now
                    && (x.EndsAtUtc == null || x.EndsAtUtc > now),
                cancellationToken))
        {
            return true;
        }

        // v1006 compatibility: do not lock existing Recomp customers out
        // merely because they predate the entitlement system.
        return await db.UserClientAccess
            .AsNoTracking()
            .AnyAsync(
                x =>
                    x.UserId == userId
                    && x.RevokedAtUtc == null,
                cancellationToken);
    }

    public async Task<AccessCodeValidationResult> ValidateCodeAsync(
        string? enteredCode,
        CancellationToken cancellationToken = default)
    {
        var value = enteredCode?.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return new AccessCodeValidationResult(
                false,
                "Enter an access code.");
        }

        var now = DateTime.UtcNow;

        if (value.Length == 6 && value.All(char.IsDigit))
        {
            var giveaway = await db.GiveawayCodes
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.Code == value,
                    cancellationToken);

            if (giveaway is null)
            {
                return new AccessCodeValidationResult(
                    false,
                    "That access code is not valid.");
            }

            if (giveaway.StatusCode != "AVAILABLE")
            {
                return new AccessCodeValidationResult(
                    false,
                    "That access code is no longer available.");
            }

            if (giveaway.RedeemByUtc.HasValue
                && giveaway.RedeemByUtc.Value < now)
            {
                return new AccessCodeValidationResult(
                    false,
                    "That access code has expired.");
            }

            return new AccessCodeValidationResult(
                true,
                "Access code recognized.",
                giveaway.Code,
                null,
                giveaway.AccessDurationDays);
        }

        var normalizedCode = value.ToUpperInvariant();

        var promo = await db.PromoCodes
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Code == normalizedCode,
                cancellationToken);

        if (promo is null)
        {
            return new AccessCodeValidationResult(
                false,
                "That promotion code is not valid.");
        }

        if (promo.StatusCode != "ACTIVE")
        {
            return new AccessCodeValidationResult(
                false,
                "That promotion is no longer available.");
        }

        if (promo.RedeemByUtc.HasValue
            && promo.RedeemByUtc.Value < now)
        {
            return new AccessCodeValidationResult(
                false,
                "That promotion has expired.");
        }

        if (promo.RedemptionCount >= promo.MaxRedemptions)
        {
            return new AccessCodeValidationResult(
                false,
                "That promotion has reached its redemption limit.");
        }

        return new AccessCodeValidationResult(
            true,
            "Promotion recognized.",
            promo.Code,
            promo.PromotionName,
            promo.AccessDurationDays);
    }
    public async Task<AccessRedemptionResult> RedeemAsync(
        UserAccount user,
        string? enteredCode,
        CancellationToken cancellationToken = default)
    {
        var value = enteredCode?.Trim();

        if (string.IsNullOrWhiteSpace(value))
        {
            return new AccessRedemptionResult(
                false,
                "Enter an access code.");
        }

        if (value.Length == 6 && value.All(char.IsDigit))
        {
            return await RedeemGiveawayAsync(
                user,
                value,
                cancellationToken);
        }

        return await RedeemPromotionAsync(
            user,
            value.ToUpperInvariant(),
            cancellationToken);
    }

    private async Task<AccessRedemptionResult> RedeemGiveawayAsync(
        UserAccount user,
        string codeValue,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
        // A retried attempt must reload database state after a rolled-back save.
        db.ChangeTracker.Clear();
        await using var transaction =
            await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var now = DateTime.UtcNow;

        var code = await db.GiveawayCodes
            .SingleOrDefaultAsync(
                x => x.Code == codeValue,
                cancellationToken);

        if (code is null)
        {
            return new AccessRedemptionResult(
                false,
                "That access code is not valid.");
        }

        if (code.StatusCode == "REDEEMED")
        {
            // A transient failure after commit may retry this same redemption.
            if (code.RedeemedByUserId == user.UserId)
                return new AccessRedemptionResult(true, "Access is already active for your account.", code.AccessExpiresAtUtc);

            return new AccessRedemptionResult(false, "That access code has already been used.");
        }

        if (code.StatusCode == "REVOKED")
        {
            return new AccessRedemptionResult(
                false,
                "That access code is no longer available.");
        }

        if (code.StatusCode == "EXPIRED"
            || (code.RedeemByUtc.HasValue
                && code.RedeemByUtc.Value < now))
        {
            if (code.StatusCode != "EXPIRED")
            {
                code.StatusCode = "EXPIRED";
                code.UpdatedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return new AccessRedemptionResult(
                false,
                "That access code has expired.");
        }

        if (code.StatusCode != "AVAILABLE")
        {
            return new AccessRedemptionResult(
                false,
                "That access code is not available.");
        }

        var accessEnds = now.AddDays(code.AccessDurationDays);

        var entitlement = new UserEntitlement
        {
            UserEntitlementId = Guid.NewGuid(),
            UserId = user.UserId,
            EntitlementTypeCode = "FULL_ACCESS",
            SourceTypeCode = "GIVEAWAY_CODE",
            SourceId = code.GiveawayCodeId,
            StartsAtUtc = now,
            EndsAtUtc = accessEnds,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        code.StatusCode = "REDEEMED";
        code.RedeemedByUserId = user.UserId;
        code.RedeemedAtUtc = now;
        code.ActivatedAtUtc = now;
        code.AccessExpiresAtUtc = accessEnds;
        code.UpdatedAtUtc = now;

        db.UserEntitlements.Add(entitlement);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AccessRedemptionResult(
            true,
            $"Access activated for {code.AccessDurationDays} days.",
            accessEnds);
        });
    }

    private async Task<AccessRedemptionResult> RedeemPromotionAsync(
        UserAccount user,
        string codeValue,
        CancellationToken cancellationToken)
    {
        var strategy = db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
        // A retried attempt must reload database state after a rolled-back save.
        db.ChangeTracker.Clear();
        await using var transaction =
            await db.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        var now = DateTime.UtcNow;

        var promo = await db.PromoCodes
            .SingleOrDefaultAsync(
                x => x.Code == codeValue,
                cancellationToken);

        if (promo is null)
        {
            return new AccessRedemptionResult(
                false,
                "That promotion code is not valid.");
        }

        var existingRedemption = await db.PromoCodeRedemptions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.PromoCodeId == promo.PromoCodeId && x.UserId == user.UserId,
                cancellationToken);

        if (existingRedemption is not null)
        {
            var existingEntitlement = await db.UserEntitlements.AsNoTracking()
                .SingleOrDefaultAsync(x => x.UserEntitlementId == existingRedemption.UserEntitlementId, cancellationToken);
            return new AccessRedemptionResult(
                true,
                "This promotion is already active for your account.",
                existingEntitlement?.EndsAtUtc);
        }

        if (promo.StatusCode == "DISABLED")
        {
            return new AccessRedemptionResult(
                false,
                "That promotion is no longer available.");
        }

        if (promo.StatusCode == "EXPIRED"
            || (promo.RedeemByUtc.HasValue
                && promo.RedeemByUtc.Value < now))
        {
            if (promo.StatusCode != "EXPIRED")
            {
                promo.StatusCode = "EXPIRED";
                promo.UpdatedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return new AccessRedemptionResult(
                false,
                "That promotion has expired.");
        }

        if (promo.StatusCode == "EXHAUSTED"
            || promo.RedemptionCount >= promo.MaxRedemptions)
        {
            if (promo.StatusCode != "EXHAUSTED")
            {
                promo.StatusCode = "EXHAUSTED";
                promo.UpdatedAtUtc = now;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return new AccessRedemptionResult(
                false,
                "That promotion has reached its redemption limit.");
        }

        if (promo.StatusCode != "ACTIVE")
        {
            return new AccessRedemptionResult(
                false,
                "That promotion is not currently available.");
        }

        var accessEnds = now.AddDays(promo.AccessDurationDays);

        var entitlement = new UserEntitlement
        {
            UserEntitlementId = Guid.NewGuid(),
            UserId = user.UserId,
            EntitlementTypeCode = "FULL_ACCESS",
            SourceTypeCode = "PROMO_CODE",
            SourceId = promo.PromoCodeId,
            StartsAtUtc = now,
            EndsAtUtc = accessEnds,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };

        var redemption = new PromoCodeRedemption
        {
            PromoCodeRedemptionId = Guid.NewGuid(),
            PromoCodeId = promo.PromoCodeId,
            UserId = user.UserId,
            UserEntitlementId = entitlement.UserEntitlementId,
            RedeemedAtUtc = now
        };

        promo.RedemptionCount++;

        if (promo.RedemptionCount >= promo.MaxRedemptions)
            promo.StatusCode = "EXHAUSTED";

        promo.UpdatedAtUtc = now;

        db.UserEntitlements.Add(entitlement);
        db.PromoCodeRedemptions.Add(redemption);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new AccessRedemptionResult(
            true,
            $"Access activated for {promo.AccessDurationDays} days.",
            accessEnds);
        });
    }
}
