namespace WebRecomp.Web.ViewModels;

public sealed class AdminEngine30EnergyPolicyPageViewModel
{
    public IReadOnlyList<AdminEngine30EnergyPolicyEditModel> Plans { get; set; } =
        Array.Empty<AdminEngine30EnergyPolicyEditModel>();
}

public sealed class AdminEngine30EnergyPolicyEditModel
{
    public string PlanCode { get; set; } = "";
    public string PlanDisplayName { get; set; } = "";

    // Entered/displayed as 0-100 percentages.
    public decimal SurplusRecoveryPct { get; set; }

    public decimal MajorTargetPct { get; set; }
    public decimal MajorMinimumPct { get; set; }
    public decimal MajorMaximumPct { get; set; }
    public int MajorPriority { get; set; }
    public decimal MajorCarryoverCapKcal { get; set; }

    public decimal AccessoryTargetPct { get; set; }
    public decimal AccessoryMinimumPct { get; set; }
    public decimal AccessoryMaximumPct { get; set; }
    public int AccessoryPriority { get; set; }
    public decimal AccessoryCarryoverCapKcal { get; set; }

    public decimal ConditioningTargetPct { get; set; }
    public decimal ConditioningMinimumPct { get; set; }
    public decimal ConditioningMaximumPct { get; set; }
    public int ConditioningPriority { get; set; }
    public decimal ConditioningCarryoverCapKcal { get; set; }

    public decimal RestTargetPct { get; set; }
    public decimal RestMinimumPct { get; set; }
    public decimal RestMaximumPct { get; set; }
    public int RestPriority { get; set; }
    public decimal RestCarryoverCapKcal { get; set; }
}
