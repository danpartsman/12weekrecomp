using WebRecomp.Web.Services.Engine30;
using Xunit;

namespace WebRecomp.Web.Tests;

/// <summary>
/// 12WR v1009 workout class archetype layer regression tests.
/// </summary>
public sealed class Engine30WorkoutClassArchetypeCatalogTests
{
    private readonly Engine30WorkoutClassArchetypeCatalog _catalog =
        new();

    [Fact]
    public void CatalogContainsFiveInitialUserFacingArchetypes()
    {
        var all =
            _catalog.GetAll();

        Assert.Equal(
            5,
            all.Count);

        Assert.Equal(
            5,
            all.Select(x => x.ArchetypeCode)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
    }

    [Theory]
    [InlineData("STRENGTH_CLASS", "RESISTANCE_CIRCUIT")]
    [InlineData("HYBRID_CONDITIONING_CLASS", "MIXED_GYM_SESSION")]
    [InlineData("INDOOR_CYCLING_CLASS", "CYCLING_INTERVALS")]
    [InlineData("BOXING_KICKBOXING_CLASS", "MIXED_CONDITIONING")]
    [InlineData("PILATES_BARRE_CLASS", "ACCESSORY")]
    public void EveryArchetypeResolvesToAnExistingTestedEngineProfile(
        string archetypeCode,
        string expectedWorkoutTypeCode)
    {
        var definition =
            _catalog.GetRequired(
                archetypeCode);

        Assert.Equal(
            expectedWorkoutTypeCode,
            definition.DefaultWorkoutTypeCode);

        var resolved =
            _catalog.ResolveDefaultWorkoutType(
                archetypeCode);

        Assert.Equal(
            expectedWorkoutTypeCode,
            resolved.WorkoutTypeCode);
    }

    [Fact]
    public void StrengthClassCarriesOtfStrength50AndF45ResistanceAsExamples()
    {
        var definition =
            _catalog.GetRequired(
                Engine30WorkoutClassArchetypeCodes.StrengthClass);

        Assert.Contains(
            definition.Examples,
            x => x.Contains(
                "Strength 50",
                StringComparison.OrdinalIgnoreCase));

        Assert.Contains(
            definition.Examples,
            x => x.Contains(
                "F45 Resistance",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void HybridClassUsesGenericCompositeRatherThanOrangetheorySpecificDefault()
    {
        var definition =
            _catalog.GetRequired(
                Engine30WorkoutClassArchetypeCodes.HybridConditioningClass);

        Assert.Equal(
            "MIXED_GYM_SESSION",
            definition.DefaultWorkoutTypeCode);

        Assert.NotEqual(
            "ORANGETHEORY",
            definition.DefaultWorkoutTypeCode);
    }

    [Fact]
    public void BoxingMappingIsExplicitlyProvisional()
    {
        var definition =
            _catalog.GetRequired(
                Engine30WorkoutClassArchetypeCodes.BoxingKickboxingClass);

        Assert.Contains(
            "dedicated",
            definition.MappingNote,
            StringComparison.OrdinalIgnoreCase);

        Assert.Contains(
            "calibrated",
            definition.MappingNote,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NewArchetypesDefaultToGenericRatherThanBrandSpecificProfiles()
    {
        // ORANGETHEORY is an intentional pre-existing Engine 3.0 profile
        // because its known treadmill/rower/floor structure has dedicated
        // physiology. The new archetype layer must not make a branded
        // profile the DEFAULT for a broad class category.
        var legacyBrandSpecificCodes =
            new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                "ORANGETHEORY"
            };

        foreach (var archetype in _catalog.GetAll())
        {
            Assert.DoesNotContain(
                archetype.DefaultWorkoutTypeCode,
                legacyBrandSpecificCodes);
        }
    }
}