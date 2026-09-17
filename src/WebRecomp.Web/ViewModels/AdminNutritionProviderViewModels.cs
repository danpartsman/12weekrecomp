using WebRecomp.NutritionIntelligence.Persistence;

namespace WebRecomp.Web.ViewModels;

public sealed class AdminNutritionProvidersViewModel
{
    public string PrimaryProviderCode { get; set; } = "";
    public string SecondaryProviderCode { get; set; } = "";
    public IReadOnlyList<NutritionProviderPolicy> Providers { get; set; } =
        Array.Empty<NutritionProviderPolicy>();
}

public sealed class AdminNutritionProviderOrderInput
{
    public string PrimaryProviderCode { get; set; } = "";
    public string SecondaryProviderCode { get; set; } = "";
}
