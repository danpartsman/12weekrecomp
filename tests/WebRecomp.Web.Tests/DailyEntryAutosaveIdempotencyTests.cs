using Xunit;

namespace WebRecomp.Web.Tests;

public sealed class DailyEntryAutosaveIdempotencyTests
{
    [Fact]
    public void DailyEntry_PersistsWorkoutIdentityBeforeOptionalRefresh()
    {
        var root = FindRepoRoot();
        var view = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "Views",
                "DailyEntry",
                "Index.cshtml"));

        Assert.DoesNotContain(
            "<script>\n<script>",
            view,
            StringComparison.Ordinal);

        var idAssignment =
            view.IndexOf(
                "payload.workoutSessionIds.forEach",
                StringComparison.Ordinal);

        var refresh =
            view.IndexOf(
                "void refreshDailyEntryCompletion()",
                StringComparison.Ordinal);

        Assert.True(idAssignment >= 0);
        Assert.True(refresh > idAssignment);

        Assert.Contains(
            "Workouts[i].SourceRecordId",
            view,
            StringComparison.Ordinal);

        Assert.Contains(
            "workoutSourceRecordIds",
            view,
            StringComparison.Ordinal);
    }

    [Fact]
    public void DailyEntry_ServerUsesStableManualSourceKeyForRepeatedPosts()
    {
        var root = FindRepoRoot();
        var controller = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "Controllers",
                "DailyEntryController.cs"));

        Assert.Contains(
            "x.SourceRecordId == sourceRecordId",
            controller,
            StringComparison.Ordinal);

        Assert.Contains(
            "NewManualWorkoutSourceRecordId",
            controller,
            StringComparison.Ordinal);

        Assert.Contains(
            "workoutSourceRecordIds",
            controller,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SharedAutosave_DoesNotTurnSuccessfulSaveIntoFailureWhenUiCallbackThrows()
    {
        var root = FindRepoRoot();
        var source = File.ReadAllText(
            Path.Combine(
                root,
                "src",
                "WebRecomp.Web",
                "wwwroot",
                "js",
                "recomp-autosave.js"));

        Assert.Contains(
            "Autosave afterSave callback failed.",
            source,
            StringComparison.Ordinal);

        Assert.Contains(
            "catch (callbackError)",
            source,
            StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var current =
            new DirectoryInfo(
                AppContext.BaseDirectory);

        while (current is not null)
        {
            var candidate =
                Path.Combine(
                    current.FullName,
                    "src",
                    "WebRecomp.Web",
                    "WebRecomp.Web.csproj");

            if (File.Exists(candidate))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the 12WR repository root.");
    }
}
