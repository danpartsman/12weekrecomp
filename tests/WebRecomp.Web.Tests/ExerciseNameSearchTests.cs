using WebRecomp.Web.Services.WorkoutIntelligence;
using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class ExerciseNameSearchTests
{
    [Theory]
    [InlineData("lateral raise dumbbell", "Dumbbell Lateral Raise", "Split Squat with Dumbbells")]
    [InlineData("lateral raise dumbbell", "Dumbbell Lateral Raise", "Cable Lateral Raise")]
    [InlineData("dumbbell lateral raise", "Dumbbell Lateral Raise", "Arnold Dumbbell Press")]
    [InlineData("bench press", "Barbell Bench Press", "Dumbbell Shoulder Press")]
    public void FullNameTokenMatchRanksAboveUnrelatedExercise(string query, string expected, string unrelated)
    {
        Assert.True(ExerciseNameSearch.Score(query, expected) > ExerciseNameSearch.Score(query, unrelated));
        Assert.True(ExerciseNameSearch.Score(query, expected) >= 800);
    }

    [Fact]
    public void ExactPhraseRanksAboveReorderedName()
    {
        Assert.True(ExerciseNameSearch.Score("Dumbbell Lateral Raise", "Dumbbell Lateral Raise")
            > ExerciseNameSearch.Score("Dumbbell Lateral Raise", "Lateral Raise Dumbbell"));
    }

    [Fact]
    public void PluralAndDbAliasAreRecognized()
    {
        Assert.True(ExerciseNameSearch.Score("DB lateral raises", "Dumbbell Lateral Raise") >= 800);
    }
}
