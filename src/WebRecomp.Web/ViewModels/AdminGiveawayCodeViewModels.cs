using System.ComponentModel.DataAnnotations;

namespace WebRecomp.Web.ViewModels;

public sealed class AdminGiveawayCodeGenerateInput
{
    [Range(1, 1000)]
    public int Quantity { get; set; } = 10;

    [Range(1, 365)]
    public int AccessDurationDays { get; set; } = 30;

    [StringLength(120)]
    public string? BatchName { get; set; }

    [DataType(DataType.Date)]
    public DateOnly? RedeemByDate { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public sealed class AdminGiveawayCodeRow
{
    public Guid GiveawayCodeId { get; set; }
    public Guid GiveawayCodeBatchId { get; set; }

    public required string Code { get; set; }
    public required string StatusCode { get; set; }

    public string? BatchName { get; set; }
    public int AccessDurationDays { get; set; }

    public DateTime? RedeemByUtc { get; set; }

    public string? RedeemedByEmail { get; set; }
    public DateTime? RedeemedAtUtc { get; set; }
    public DateTime? AccessExpiresAtUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AdminGiveawayCodesViewModel
{
    public AdminGiveawayCodeGenerateInput Generate { get; set; } = new();

    public AdminPromoCodeGenerateInput PromoGenerate { get; set; } = new();

    public IReadOnlyList<AdminGiveawayCodeRow> Codes { get; set; }
        = Array.Empty<AdminGiveawayCodeRow>();

    public IReadOnlyList<AdminPromoCodeRow> PromoCodes { get; set; }
        = Array.Empty<AdminPromoCodeRow>();
}

public sealed class AdminPromoCodeGenerateInput
{
    [Required]
    [StringLength(120)]
    public string PromotionName { get; set; } = "";

    [Required]
    [StringLength(32, MinimumLength = 4)]
    [RegularExpression(
        "^(?=.*[A-Za-z])[A-Za-z0-9_-]+$",
        ErrorMessage = "Promo code must contain at least one letter and may use letters, numbers, hyphens, or underscores.")]
    public string Code { get; set; } = "";

    [Range(1, 365)]
    public int AccessDurationDays { get; set; } = 30;

    [Range(1, 1000000)]
    public int MaxRedemptions { get; set; } = 1000;

    [DataType(DataType.Date)]
    public DateOnly? RedeemByDate { get; set; }

    [StringLength(1000)]
    public string? Notes { get; set; }
}

public sealed class AdminPromoCodeRow
{
    public Guid PromoCodeId { get; set; }
    public required string PromotionName { get; set; }
    public required string Code { get; set; }
    public required string StatusCode { get; set; }

    public int AccessDurationDays { get; set; }
    public int MaxRedemptions { get; set; }
    public int RedemptionCount { get; set; }

    public DateTime? RedeemByUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public sealed class AdminPromoCodeDetailViewModel
{
    public Guid PromoCodeId { get; set; }

    public required string PromotionName { get; set; }
    public required string Code { get; set; }
    public required string StatusCode { get; set; }

    public int AccessDurationDays { get; set; }
    public int MaxRedemptions { get; set; }
    public int RedemptionCount { get; set; }

    public DateTime? RedeemByUtc { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public IReadOnlyList<AdminPromoRedemptionRow> Redemptions { get; set; }
        = Array.Empty<AdminPromoRedemptionRow>();
}

public sealed class AdminPromoRedemptionRow
{
    public string? UserEmail { get; set; }

    public DateTime RedeemedAtUtc { get; set; }

    public DateTime StartsAtUtc { get; set; }
    public DateTime? EndsAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public required string AccessStatus { get; set; }
}
