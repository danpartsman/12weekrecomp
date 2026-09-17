using WebRecomp.NutritionIntelligence.Persistence;

namespace WebRecomp.NutritionIntelligence.Tests;

public sealed class NutritionPersistenceContractTests
{
    [Fact]
    public void Reference_RequiresExactlyOneTarget()
    {
        Assert.Throws<InvalidOperationException>(
            () => new NutritionFoodReference().Validate());

        Assert.Throws<InvalidOperationException>(
            () => new NutritionFoodReference(
                    ExternalFoodId: Guid.NewGuid(),
                    CustomFoodId: Guid.NewGuid())
                .Validate());

        new NutritionFoodReference(
                ExternalFoodId: Guid.NewGuid())
            .Validate();

        new NutritionFoodReference(
                CustomFoodId: Guid.NewGuid())
            .Validate();

        new NutritionFoodReference(
                RecipeId: Guid.NewGuid())
            .Validate();
    }

    [Fact]
    public void RecipeItems_CannotReferenceRecipesRecursively()
    {
        var reference =
            new NutritionFoodReference(
                RecipeId: Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(
            () => reference.Validate(allowRecipe: false));
    }

    [Theory]
    [InlineData(NutritionCachePolicyCodes.Persistent, true)]
    [InlineData(NutritionCachePolicyCodes.Temporary, true)]
    [InlineData(NutritionCachePolicyCodes.ReferenceOnly, false)]
    [InlineData(NutritionCachePolicyCodes.SessionOnly, false)]
    public void ProviderPayloadPersistence_ObeysCachePolicy(
        string cachePolicy,
        bool expected)
    {
        Assert.Equal(
            expected,
            NutritionPersistencePolicy.MayPersistProviderPayload(
                cachePolicy));
    }

    [Fact]
    public void ReferenceOnlyProvider_DoesNotReceivePersistedSnapshotPolicy()
    {
        Assert.Equal(
            NutritionSnapshotPolicyCodes.ReferenceOnly,
            NutritionPersistencePolicy.SnapshotPolicyFor(
                NutritionCachePolicyCodes.ReferenceOnly,
                isUserEntered: false));
    }

    [Fact]
    public void UserEnteredFood_IsAlwaysFirstPartySnapshot()
    {
        Assert.Equal(
            NutritionSnapshotPolicyCodes.UserEntered,
            NutritionPersistencePolicy.SnapshotPolicyFor(
                NutritionCachePolicyCodes.ReferenceOnly,
                isUserEntered: true));
    }

    [Fact]
    public void TemporaryProvider_UsesReferenceOnlyDailySnapshotPolicy()
    {
        Assert.Equal(
            NutritionSnapshotPolicyCodes.ReferenceOnly,
            NutritionPersistencePolicy.SnapshotPolicyFor(
                NutritionCachePolicyCodes.Temporary,
                isUserEntered: false));
    }

}
