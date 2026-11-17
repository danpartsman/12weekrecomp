using Microsoft.EntityFrameworkCore;
using WebRecomp.Web.Data;
using WebRecomp.Web.Models;
using WebRecomp.Web.Services;
using WebRecomp.Web.Services.Coaching;
using WebRecomp.Web.Services.Dashboard3;
using WebRecomp.Web.Services.WorkoutIntelligence;
namespace WebRecomp.Web.Tests;

public sealed class RecompWorkoutReviewTests
{
    private static readonly DateOnly Today=new(2026,11,17);
    private static WorkoutSession Session(string type,decimal minutes=30,int offset=0,string? system=null)=>new(){WorkoutSessionId=Guid.NewGuid(),ClientId=Guid.NewGuid(),ProgramId=Guid.NewGuid(),WorkoutTypeCode=type,SystemWorkoutCode=system,WorkoutDate=Today.AddDays(offset),DurationMin=minutes,CompletedFlag=true};
    private static RecompWorkoutReview Review(params WorkoutSession[] sessions)=>RecompWorkoutReviewService.Build(Today,Today.AddDays(-30),sessions,[],[],[]);
    [Fact] public void MissingEachRequirementCostsExactlyFivePoints()
    {
        var empty=Review();Assert.Equal(30,empty.Deductions);Assert.Equal(70,empty.Score);Assert.Equal(6,empty.Requirements.Count);Assert.All(empty.Requirements,r=>Assert.Equal(5,r.Deduction));
        var push=Review(Session("MAJOR_PUSH"));Assert.Equal(25,push.Deductions);Assert.Equal(75,push.Score);
    }
    [Fact] public void AllSixRequirementsAndExtraWorkCapAtOneHundred()
    {
        var full=Session("FULL_BODY_STRENGTH");var a=Session("ORANGETHEORY",60,-1);var b=Session("TRUE_HIIT",30,-2);var rest=Session("REST",0,-3);
        var result=Review(full,a,b,rest,Session("GENERAL_CARDIO",60,-4));
        Assert.Equal(100,result.Score);Assert.Equal(6,result.MetCount);Assert.Equal(0,result.Deductions);
    }
    [Fact] public void CompletedTodayCountsButOldFutureAndUnfinishedSessionsDoNot()
    {
        Assert.Equal(75,Review(Session("MAJOR_PUSH",30,0)).Score);
        var unfinished=Session("FULL_BODY_STRENGTH");unfinished.CompletedFlag=false;
        Assert.Equal(70,Review(unfinished,Session("FULL_BODY_STRENGTH",60,-7),Session("FULL_BODY_STRENGTH",60,1)).Score);
    }
    [Theory] [InlineData("SYS_OTF_STRENGTH_50")] [InlineData("OTF_STRENGTH50_2026_09_30")]
    public void StrengthFiftyCoversStrengthButIsNotACardioSession(string system)
    {
        var result=Review(Session("RESISTANCE_CIRCUIT",50,system:system));
        Assert.All(result.Requirements.Take(3),r=>Assert.True(r.Met));Assert.All(result.Requirements.Skip(3),r=>Assert.False(r.Met));Assert.Equal(85,result.Score);
    }
    [Fact] public void UpperLowerAndSplitSessionLogsOnlyCoverTheirGroups()
    {
        var upper=Review(Session("OTF_STRENGTH_UPPER",50));Assert.True(upper.Requirements[0].Met);Assert.True(upper.Requirements[1].Met);Assert.False(upper.Requirements[2].Met);
        var lower=Review(Session("OTF_STRENGTH_LOWER",50));Assert.False(lower.Requirements[0].Met);Assert.True(lower.Requirements[2].Met);
        var split=Review(Session("FULL_BODY_STRENGTH",40,system:SystemWorkoutRecipeCatalog.ChestTricepsGlutesHamstringsCode));
        Assert.True(split.Requirements[0].Met);Assert.False(split.Requirements[1].Met);Assert.True(split.Requirements[2].Met);
    }
    [Theory] [InlineData("ORANGETHEORY")] [InlineData("OTF_3G")] [InlineData("ZONE2_CARDIO")] [InlineData("RUNNING")] [InlineData("SWIMMING")]
    public void StandardClassesAndCardioCountWithoutInventingStrength(string type)
    {
        var result=Review(Session(type));Assert.Equal(75,result.Score);Assert.True(result.Requirements[3].Met);Assert.False(result.Requirements[4].Met);Assert.All(result.Requirements.Take(3),r=>Assert.False(r.Met));
    }
    [Fact] public void TinySessionsAndLargeCalorieEstimatesDoNotBypassTheChecklist()
    {
        var shortSession=Session("FULL_BODY_STRENGTH",1);shortSession.DeviceCalories=5000;
        Assert.Equal(70,Review(shortSession,Session("TRUE_HIIT",1)).Score);
    }
    [Fact] public void WorkingRepsAccumulateAcrossSessionsAndNeverFromFuturePlans()
    {
        var a=Session("ACCESSORY");var b=Session("ACCESSORY",30,-1);
        var work=new[]{new RecompWorkingReps(a.WorkoutSessionId,"Press",["push"],30),new RecompWorkingReps(b.WorkoutSessionId,"Press",["push"],30),new RecompWorkingReps(Guid.NewGuid(),"Not completed",["pull"],100)};
        var result=RecompWorkoutReviewService.Build(Today,Today.AddDays(-30),[a,b],[],[],work);
        Assert.True(result.Requirements[0].Met);Assert.False(result.Requirements[1].Met);Assert.Contains("60 / 60",result.Requirements[0].Evidence);
        var below=RecompWorkoutReviewService.Build(Today,Today.AddDays(-30),[a],[],[],[work[0] with{Reps=59}]);Assert.False(below.Requirements[0].Met);
    }
    [Fact] public void SnapGenericFullBodyTypeCannotClaimUntrainedGroups()
    {
        var snap=Session("FULL_BODY_STRENGTH",60);snap.EntrySourceCode=SnapWorkoutEngine.Source;
        var result=RecompWorkoutReviewService.Build(Today,Today.AddDays(-30),[snap],[],[],[new(snap.WorkoutSessionId,"Curl",["pull"],60)]);
        Assert.False(result.Requirements[0].Met);Assert.True(result.Requirements[1].Met);Assert.False(result.Requirements[2].Met);
    }
    [Fact] public void DetailedRowsDoNotReceiveTypeOnlyFallbackCredit()
    {
        var s=Session("FULL_BODY_STRENGTH",60);
        var review=RecompWorkoutReviewService.Build(Today,Today.AddDays(-30),[s],[],[],[new(s.WorkoutSessionId,"Press",["push"],30)],new HashSet<Guid>{s.WorkoutSessionId});
        Assert.All(review.Requirements.Take(3),r=>Assert.False(r.Met));
    }
    [Fact] public void RestRequiresConfirmationAndCannotOverlapAWorkout()
    {
        Assert.False(Review().Requirements.Last().Met);
        Assert.False(Review(Session("REST",0),Session("MAJOR_PUSH")).Requirements.Last().Met);
        Assert.True(Review(Session("REST",0),Session("MOBILITY_RECOVERY",10)).Requirements.Last().Met);
        var plans=new[]{new PlannedWorkout{PlannedDate=Today.AddDays(-1),WorkoutTypeCode="REST"}};
        var checkedIn=RecompWorkoutReviewService.Build(Today,Today.AddDays(-30),[],plans,[new DailyCheckin{CheckinDate=Today.AddDays(-1)}],[]);
        Assert.True(checkedIn.Requirements.Last().Met);
        var todayPlan=new[]{new PlannedWorkout{PlannedDate=Today,WorkoutTypeCode="REST"}};
        Assert.False(RecompWorkoutReviewService.Build(Today,Today.AddDays(-30),[],todayPlan,[new DailyCheckin{CheckinDate=Today}],[]).Requirements.Last().Met);
    }
    [Fact] public void ActualWorkingRepsPreventRestCreditEvenWhenDurationIsMissing()
    {
        var lift=Session("MAJOR_PUSH",0);var rest=Session("REST",0);
        var review=RecompWorkoutReviewService.Build(Today,Today.AddDays(-30),[lift,rest],[],[],[new(lift.WorkoutSessionId,"Press",["push"],60)]);
        Assert.True(review.Requirements[0].Met);Assert.False(review.Requirements.Last().Met);
    }
    [Fact] public void NewProgramShowsProgressWithoutAnEarlyPenaltyOrPerfectWorkoutScore()
    {
        var result=RecompWorkoutReviewService.Build(Today,Today.AddDays(-2),[],[],[],[]);
        Assert.False(result.FullWindow);Assert.Null(result.Score);Assert.Equal(0,result.Deductions);
    }
    [Fact] public void OverallScoreWeightsWorkoutCoverageAtThirtyFivePercent()
    {
        var all=Review(Session("FULL_BODY_STRENGTH"),Session("GENERAL_CARDIO",30,-1),Session("ORANGETHEORY",60,-2),Session("REST",0,-3));
        var missingRest=Review(Session("FULL_BODY_STRENGTH"),Session("GENERAL_CARDIO",30,-1),Session("ORANGETHEORY",60,-2));
        var days=new[]{new RecompDayEvidence(Today.AddDays(-1),200,200,60,2000,180,200,60,2000,0,"test")};
        var before=RecompAdherenceScorer.Score(Today,days,[],workoutReview:all);
        var after=RecompAdherenceScorer.Score(Today,days,[],workoutReview:missingRest);
        Assert.Equal(100,before.Score);Assert.Equal(98,after.Score);Assert.Equal(95,after.WorkoutScore);Assert.Equal(100,after.NutritionScore);
        Assert.Equal(70,RecompAdherenceScorer.Score(Today,[],[],workoutReview:Review()).Score);
    }
    [Fact] public void CapturedV8AndEarlierScoresKeepTheirOriginalComponentsAndChecklist()
    {
        var review=Review();var score=RecompAdherenceScorer.Score(Today,[],[],workoutReview:review);
        var snapshot=new RecompScoreSnapshot(RecompScoreResult.Version,Today.AddDays(-7),70,0,7,[],[],score.Components,WorkoutReview:review);
        var restored=RecompCockpitService.RestoreCapturedScore(Today,snapshot);Assert.Equal(70,restored.WorkoutScore);Assert.Equal(30,restored.WorkoutReview!.Deductions);
        var legacy=snapshot with{Version="RECOMP_SCORE_V7",Score=42,WorkoutReview=null};
        Assert.Equal(42,RecompCockpitService.RestoreCapturedScore(Today,legacy).Score);
    }
    [Theory] [InlineData("Rear delt reverse fly","pull")] [InlineData("Romanian deadlift","legs")] [InlineData("Dumbbell curl","pull")] [InlineData("Shoulder press","push")]
    public void RecognizedExerciseMovementMapsToTheCorrectGroup(string exercise,string group)
    {
        Assert.Equal(new[]{group},RecompWorkoutReviewService.ExerciseGroups(exercise));
    }
    [Fact] public async Task DatabaseEvidenceExcludesWarmupsAndUsesPrimaryMuscles()
    {
        using var db=new RecompDbContext(new DbContextOptionsBuilder<RecompDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var client=new Client{ClientId=Guid.NewGuid(),ClientName="Example"};var program=new ProgramRecord{ProgramId=Guid.NewGuid(),ClientId=client.ClientId,StartDate=Today.AddDays(-30),SelectedPlanCode="TEST",GoalCode="ATHLETIC_RECOMP"};
        var s=Session("FULL_BODY_STRENGTH");s.ClientId=client.ClientId;s.ProgramId=program.ProgramId;
        var exercise=new DetailedWorkoutExerciseRecord{DetailedWorkoutExerciseId=Guid.NewGuid(),WorkoutSessionId=s.WorkoutSessionId,ExerciseCatalogItemId=Guid.NewGuid(),ExerciseNameSnapshot="Example press",Sets=[]};
        exercise.Sets.Add(new(){DetailedWorkoutSetId=Guid.NewGuid(),DetailedWorkoutExerciseId=exercise.DetailedWorkoutExerciseId,Reps=100,IsWarmup=true});
        exercise.Sets.Add(new(){DetailedWorkoutSetId=Guid.NewGuid(),DetailedWorkoutExerciseId=exercise.DetailedWorkoutExerciseId,Reps=30});
        db.AddRange(client,program,s,exercise,new ExerciseMuscleTargetRecord{ExerciseMuscleTargetId=Guid.NewGuid(),ExerciseCatalogItemId=exercise.ExerciseCatalogItemId,ExternalId="chest",MuscleSlug="chest",MuscleName="Chest",RoleCode="primary"},
            new ExerciseMuscleTargetRecord{ExerciseMuscleTargetId=Guid.NewGuid(),ExerciseCatalogItemId=exercise.ExerciseCatalogItemId,ExternalId="biceps",MuscleSlug="biceps",MuscleName="Biceps",RoleCode="secondary"});
        await db.SaveChangesAsync();
        var context=new AppClientContext(new UserAccount{UserId=Guid.NewGuid(),ExternalSubject="example"},client,program);
        var review=await new RecompWorkoutReviewService(db).BuildAsync(context,Today,[s],[],[],default);
        Assert.Contains("30 / 60",review.Requirements[0].Evidence);Assert.False(review.Requirements[0].Met);Assert.False(review.Requirements[1].Met);
    }
}
