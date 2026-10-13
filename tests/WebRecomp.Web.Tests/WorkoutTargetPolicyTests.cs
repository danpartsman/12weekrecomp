using WebRecomp.Web.Models;
using WebRecomp.Web.Services.WorkoutTargeting;

namespace WebRecomp.Web.Tests;

public sealed class WorkoutTargetPolicyTests
{
    [Fact] public void LegacyNotesRemainReadable()
    {
        var result=BodyCheckinCodec.Read("Shoulder feels better.");
        Assert.Equal("Shoulder feels better.",result.Note);Assert.False(result.Checked);Assert.Empty(result.Regions);
    }
    [Fact] public void RegionalSignalsAndNoteRoundTripIndependently()
    {
        var stored=BodyCheckinCodec.Write("Slept well",true,[new(){Region="shoulders",Side="right",Pain=3,Soreness=1,MovementLimited=true}]);
        var changed=BodyCheckinCodec.UpdateNote(stored,"Updated note");var result=BodyCheckinCodec.Read(changed);
        Assert.True(result.Checked);Assert.Equal("Updated note",result.Note);Assert.Equal(3,result.Regions.Single().Pain);Assert.Equal("right",result.Regions.Single().Side);
    }
    [Fact] public void MaximumBodyCheckFitsExistingColumn()
    {
        var stored=BodyCheckinCodec.Write(new string('a',1000),true,BodyRegions.All.Select(r=>new RegionFeeling{Region=r.Code,Soreness=10,Pain=10,MovementLimited=true,SharpOrWorsening=true}));
        Assert.True(stored.Length<=2000);Assert.Equal(BodyRegions.All.Length,BodyCheckinCodec.Read(stored).Regions.Count);
    }
    [Fact] public void ExercisesAreExcludedOnlyForTaggedRegions()
    {
        var loading=BodyRegions.LoadingRegions(["Pectorals","Triceps"]);
        Assert.True(WorkoutTargetPolicy.Excluded(loading,[new(){Region="chest",Pain=2}]));
        Assert.False(WorkoutTargetPolicy.Excluded(loading,[new(){Region="shoulders",Pain=2}]));
        Assert.False(WorkoutTargetPolicy.Excluded(loading,[new(){Region="quads",Pain=2}]));
    }
    [Fact] public void EvenMildSorenessExcludesMatchingExercises()
        => Assert.True(WorkoutTargetPolicy.Excluded(["quads"],[new(){Region="quads",Soreness=1}]));
    [Theory] [InlineData(0,true,false)] [InlineData(0,false,true)] [InlineData(8,false,false)]
    public void LimitedMovementOrSevereSorenessExcludesLoading(int soreness,bool limited,bool sharp)
        => Assert.True(WorkoutTargetPolicy.Excluded(["quads"],[new(){Region="quads",Soreness=soreness,MovementLimited=limited,SharpOrWorsening=sharp}]));
    [Fact] public void LowFuelAndLowEnergyCapHardEffort()
    {
        Assert.Equal(6m,WorkoutTargetPolicy.EffortCap("hard",true,null));
        Assert.Equal(6m,WorkoutTargetPolicy.EffortCap("hard",false,new(){EnergyScore=2}));
        Assert.Equal(8m,WorkoutTargetPolicy.EffortCap("hard",false,null));
    }
    [Fact] public void PersistentPainSettingsSurviveNoteUpdates()
    {
        var stored = BodyCheckinCodec.Write(null, true, [new() { Region="quads", Mark=2, Pain=5 }], false, true, 2, 5);
        var result = BodyCheckinCodec.Read(BodyCheckinCodec.UpdateNote(stored, "note"));
        Assert.True(result.KeepPain); Assert.Equal(5, result.PainRating); Assert.Equal(2, result.Regions.Single().Mark);
    }
    [Fact] public void ExactSimplifiedRegionsAreAvailable()
    {
        Assert.Equal(6, BodyRegions.All.Count(x => x.View == "front"));
        Assert.Equal(6, BodyRegions.All.Count(x => x.View == "back"));
        Assert.Contains("upperback", BodyRegions.FromMuscles(["Latissimus dorsi"]));
    }
    [Fact] public void ArmBicepsAreDistinctFromHamstringBicepsFemoris()
    {
        Assert.Contains("biceps",BodyRegions.LoadingRegions(["Biceps brachii"]));
        var hamstring=BodyRegions.LoadingRegions(["Biceps femoris"]);
        Assert.DoesNotContain("biceps",hamstring);Assert.Contains("hamstrings",hamstring);
        var stored=BodyCheckinCodec.Write(null,true,[new(){Region="biceps",Mark=2,Pain=4}],keepPain:true,painRating:4);
        Assert.Equal("biceps",BodyCheckinCodec.Read(stored).Regions.Single().Region);
    }
    [Fact] public void PastAndCompletedPlansCannotBeEdited()
    {
        var today=new DateOnly(2026,10,12);
        Assert.False(WorkoutPlanPolicy.CanEdit(today.AddDays(-1),today,false));
        Assert.False(WorkoutPlanPolicy.CanEdit(today,today,true));
        Assert.True(WorkoutPlanPolicy.CanEdit(today,today,false));
        Assert.True(WorkoutPlanPolicy.CanEdit(today.AddDays(1),today,false));
    }
}
