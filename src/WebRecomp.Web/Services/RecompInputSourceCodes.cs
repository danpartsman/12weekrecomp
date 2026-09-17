namespace WebRecomp.Web.Services;

/// <summary>
/// Canonical provenance codes for observed client inputs.
///
/// v1010 rule:
/// preferences control which entry UI is offered for new/current data;
/// historical canonical rows keep their own source provenance. Changing a
/// current preference must not silently rewrite historical source selection.
/// </summary>
public static class RecompInputSourceCodes
{
    public const string Manual = "MANUAL";
    public const string DetailedWorkout = "DETAILED_WORKOUT";
    public const string FoodLog = "FOOD_LOG";
    public const string External = "EXTERNAL";
    public const string DemoSeed = "DEMO_SEED";
}
