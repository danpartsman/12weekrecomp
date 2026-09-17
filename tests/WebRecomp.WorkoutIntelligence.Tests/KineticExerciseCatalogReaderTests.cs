using WebRecomp.WorkoutIntelligence.Catalog;

namespace WebRecomp.WorkoutIntelligence.Tests;

public sealed class KineticExerciseCatalogReaderTests
{
    [Fact]
    public void Read_MapsKineticShapeInto12WrCatalogContract()
    {
        var json = """
        [
          {
            "id": "d586b5aa-c2f4-4cb5-8038-d10b03c3b763",
            "name": "Barbell Bench Press",
            "type": "reps",
            "difficultyLevel": "intermediate",
            "forceType": "push",
            "mechanics": "compound",
            "category": "strength",
            "instructions": ["Press the bar."],
            "muscleGroups": [
              {
                "id": "6427e37e-b443-4577-8d16-635b7607dfeb",
                "slug": "chest",
                "name": "Chest",
                "type": "primary"
              }
            ],
            "equipment": [
              {
                "id": "97df708e-50d0-43a3-a474-9b6823f519a7",
                "name": "Barbell",
                "type": "weight",
                "usageType": "single",
                "numItems": 1
              }
            ]
          }
        ]
        """;

        var snapshot = new KineticExerciseCatalogReader().Read(json);
        var item = Assert.Single(snapshot.Exercises);

        Assert.Equal("KINETIC", snapshot.SourceCode);
        Assert.Equal("MIT", snapshot.LicenseCode);
        Assert.Equal("Barbell Bench Press", item.Name);
        Assert.Equal("compound", item.Mechanics);
        Assert.Equal("chest", Assert.Single(item.MuscleTargets).Slug);
        Assert.Equal("Barbell", Assert.Single(item.Equipment).Name);
    }
}
