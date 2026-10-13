using WebRecomp.Web.Models;
using WebRecomp.Web.Services.WorkoutTargeting;
namespace WebRecomp.Web.Tests;
public sealed class WorkoutSessionComposerTests
{
    private static ExerciseCatalogItemRecord Exercise(string name,string muscle,string mechanics="compound")
    {
        var e=new ExerciseCatalogItemRecord{ExerciseCatalogItemId=Guid.NewGuid(),SourceCode="TEST",SourceExternalId=name,Name=name,MeasurementType="reps",Mechanics=mechanics};
        e.MuscleTargets.Add(new(){ExerciseMuscleTargetId=Guid.NewGuid(),ExerciseCatalogItemId=e.ExerciseCatalogItemId,ExternalId=muscle,MuscleSlug=muscle,MuscleName=muscle,RoleCode="primary"});
        return e;
    }
    [Fact] public void GeneratorBalancesMusclesOrdersCompoundsAndAvoidsExactRecentRepeat()
    {
        var press=Exercise("A press","Pectorals");var fresh=Exercise("Z press","Pectorals");
        var row=Exercise("Row","Latissimus dorsi");var squat=Exercise("Squat","Quadriceps");
        var hinge=Exercise("Hinge","Hamstrings");var fly=Exercise("Fly","Pectorals","isolation");
        var catalog=new List<ExerciseCatalogItemRecord>{press,fresh,row,squat,hinge,fly};
        var result=WorkoutSessionComposer.Generate(catalog,new(){Activity="fullbody",Minutes=35},8,new Dictionary<string,decimal>(),new Dictionary<Guid,decimal>{{press.ExerciseCatalogItemId,10}});
        Assert.Equal(fresh.ExerciseCatalogItemId,result[0].ExerciseId);
        Assert.Contains(result,e=>e.ExerciseId==row.ExerciseCatalogItemId);Assert.Contains(result,e=>e.ExerciseId==squat.ExerciseCatalogItemId);Assert.Contains(result,e=>e.ExerciseId==hinge.ExerciseCatalogItemId);
        Assert.Equal(result.Count,result.Select(e=>e.ExerciseId).Distinct().Count());
    }
    [Theory] [InlineData(10)] [InlineData(20)] [InlineData(45)]
    public void FinalSessionFitsTimeAndUsesLongerRestForHardCompounds(int minutes)
    {
        var compound=Exercise("Press","Pectorals");var isolation=Exercise("Fly","Pectorals","isolation");
        var catalog=new[]{compound,isolation}.ToDictionary(e=>e.ExerciseCatalogItemId);
        var choice=new WorkoutTargetChoice{TypeCode="MAJOR_PUSH",Rpe=8,Exercises=catalog.Values.Select(e=>new TargetExercise(e.ExerciseCatalogItemId,e.Name,e.Mechanics,4,8,8,["chest"])).ToList()};
        WorkoutSessionComposer.FitSession(choice,minutes,catalog);
        Assert.NotEmpty(choice.Exercises);Assert.InRange(choice.Minutes,1,minutes);
        Assert.Equal(choice.Minutes,WorkoutSessionComposer.EstimateMinutes(choice.Exercises,choice.WarmupMinutes));
        Assert.All(choice.Exercises.Where(e=>e.Mechanics=="compound"),e=>Assert.Equal(120,e.RestSeconds));
        Assert.All(choice.Exercises.Where(e=>e.Mechanics=="isolation"),e=>Assert.Equal(60,e.RestSeconds));
    }
    [Fact] public void SessionEffortMatchesItsReducedExercisePrescriptions()
    {
        var exercise=Exercise("Press","Pectorals");
        var choice=new WorkoutTargetChoice{TypeCode="MAJOR_PUSH",Rpe=8,Exercises=[new(exercise.ExerciseCatalogItemId,exercise.Name,exercise.Mechanics,2,10,6,["chest"])]};
        WorkoutSessionComposer.FitSession(choice,30,new Dictionary<Guid,ExerciseCatalogItemRecord>{{exercise.ExerciseCatalogItemId,exercise}});
        Assert.Equal(6,choice.Rpe);
    }
    [Fact] public void AddOnIsReservedBeforeDroppingLowerPriorityMainExercises()
    {
        var entries=Enumerable.Range(0,8).Select(i=>Exercise("Main "+i,"Pectorals")).ToList();
        var addon=Exercise("Wall Slide","Deltoids","isolation");entries.Add(addon);
        var choice=new WorkoutTargetChoice{TypeCode="MAJOR_PUSH",Rpe=8,Exercises=entries.Select(e=>new TargetExercise(e.ExerciseCatalogItemId,e.Name,e.Mechanics,3,8,e==addon?4:8,["chest"],IsAddon:e==addon)).ToList()};
        WorkoutSessionComposer.FitSession(choice,14,entries.ToDictionary(e=>e.ExerciseCatalogItemId));
        Assert.Contains(choice.Exercises,e=>e.IsAddon);Assert.Contains(choice.Exercises,e=>!e.IsAddon);Assert.True(choice.Minutes<=14);
    }
}
