namespace WebRecomp.Web.ViewModels;

public sealed class NutritionIntelligencePageViewModel
{
    public NutritionViewModel Manual { get; set; } = new();
    public bool FoodLogEnabled { get; set; }
    public bool FoodLogAvailable { get; set; }
    public bool NutritionManualOverride { get; set; }
    public string NutritionSourceCode { get; set; } = "MANUAL";
    public string NutritionSourceLabel { get; set; } = "Manual entry";
    public string? FoodQuery { get; set; }
    public string FoodSort { get; set; } = "BEST_MATCH";
    public string? FoodSearchWarning { get; set; }
    public List<NutritionFoodSearchResultViewModel> SearchResults { get; set; } = [];
    public NutritionSelectedFoodViewModel? SelectedFood { get; set; }
    public List<NutritionDailyLogItemViewModel> LoggedItems { get; set; } = [];
    public NutritionDailyTotalsViewModel? LoggedTotals { get; set; }
    public NutritionPlanTargetsViewModel? PlanTargets { get; set; }
    public string? PlanTargetWarning { get; set; }
    public List<NutritionCustomFoodViewModel> CustomFoods { get; set; } = [];
}

public sealed class NutritionFoodSearchResultViewModel
{
    public string ProviderCode { get; set; } = "";
    public string SourceFoodId { get; set; } = "";
    public string Description { get; set; } = "";
    public string? BrandOwner { get; set; }
    public string? DataType { get; set; }
    public string? GtinUpc { get; set; }
    public decimal? ServingSize { get; set; }
    public string? ServingSizeUnit { get; set; }
    public string? HouseholdServingText { get; set; }
    public decimal NutrientBasisAmount { get; set; } = 100m;
    public string NutrientBasisUnit { get; set; } = "g";
    public decimal DefaultGramAmount { get; set; } = 100m;
    public decimal? EnergyKcal { get; set; }
    public decimal? ProteinG { get; set; }
    public decimal? CarbohydrateG { get; set; }
    public decimal? FatG { get; set; }
}

public sealed class NutritionSelectedFoodViewModel
{
    public string ProviderCode { get; set; } = "";
    public string SourceFoodId { get; set; } = "";
    public string Description { get; set; } = "";
    public string? BrandOwner { get; set; }
    public string? DataType { get; set; }
    public string DefaultMeasureCode { get; set; } = "";
    public List<NutritionFoodMeasureViewModel> Measures { get; set; } = [];
}

public sealed class NutritionFoodMeasureViewModel
{
    public string Code { get; set; } = "";
    public string Description { get; set; } = "";
    public string UnitCode { get; set; } = "";
    public decimal QuantityPerSelection { get; set; }
    public decimal BasisQuantityPerSelection { get; set; }
    public string BasisUnit { get; set; } = "";
    public decimal? EnergyKcal { get; set; }
    public decimal? ProteinG { get; set; }
    public decimal? CarbohydrateG { get; set; }
    public decimal? FatG { get; set; }
}

public sealed class NutritionDailyLogItemViewModel
{
    public Guid DailyLogItemId { get; set; }
    public string MealSlotCode { get; set; } = "";
    public int ItemOrder { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public string QuantityUnitCode { get; set; } = "";
    public decimal? EnergyKcal { get; set; }
    public decimal? ProteinG { get; set; }
    public decimal? CarbohydrateG { get; set; }
    public decimal? FatG { get; set; }
}

public sealed class NutritionPlanTargetsViewModel
{
    public decimal EnergyKcal { get; set; }
    public decimal ProteinG { get; set; }
    public decimal CarbohydrateG { get; set; }
    public decimal FatG { get; set; }
    public string EngineVersionCode { get; set; } = "";
}

public sealed class NutritionDailyTotalsViewModel
{
    public decimal EnergyKcal { get; set; }
    public decimal ProteinG { get; set; }
    public decimal CarbohydrateG { get; set; }
    public decimal FatG { get; set; }
    public decimal? FiberG { get; set; }
    public string CompletenessCode { get; set; } = "";
}

public sealed class NutritionCustomFoodViewModel
{
    public Guid CustomFoodId { get; set; }
    public string FoodName { get; set; } = "";
    public string? BrandName { get; set; }
    public decimal ServingSize { get; set; }
    public string ServingUnitCode { get; set; } = "";
    public string? HouseholdServingText { get; set; }
    public decimal? EnergyKcal { get; set; }
    public decimal? ProteinG { get; set; }
    public decimal? CarbohydrateG { get; set; }
    public decimal? FatG { get; set; }
    public decimal? FiberG { get; set; }
}
