using System.Text.Json;
using System.Text.Json.Nodes;
using WebRecomp.Web.Services.Coaching;

namespace WebRecomp.Web.Services.Notifications;

public sealed record CoachNotificationChange(bool AgreementChanged, IReadOnlyList<AccountabilityWorkout> Workouts, bool RemovedWorkouts)
{
    public bool HasChanges => AgreementChanged || Workouts.Count > 0 || RemovedWorkouts;
    public static CoachNotificationChange Compare(string? beforeJson, string afterJson)
    {
        var after = AccountabilityDocument.Parse(afterJson);
        var before = beforeJson is null ? null : AccountabilityDocument.Parse(beforeJson);
        static string Agreement(AccountabilityState state) => AccountabilityDocument.Canonical(JsonSerializer.Serialize(new { state.Enabled, state.Agreement, state.Payment, state.Rules }, AccountabilityDocument.Json));
        static string Workout(AccountabilityWorkout workout) => AccountabilityDocument.Canonical(JsonSerializer.Serialize(workout, AccountabilityDocument.Json));
        var agreement = before is null
            ? after.Enabled || !string.IsNullOrWhiteSpace(after.Agreement.Start)
            : Agreement(before) != Agreement(after);
        var changed = after.SnapWorkouts.Where(w => before?.SnapWorkouts.SingleOrDefault(x => x.Id == w.Id) is not { } old || Workout(old) != Workout(w)).ToArray();
        var removed = before?.SnapWorkouts.Any(w => after.SnapWorkouts.All(x => x.Id != w.Id)) == true;
        return new(agreement, changed, removed);
    }
    public string Subject => AgreementChanged && (Workouts.Count > 0 || RemovedWorkouts) ? "Your coach updated your agreement and Snap Workouts"
        : AgreementChanged ? "Your coach updated your agreement" : "Your coach updated your Snap Workouts";
    public string EmailBody(string coach, string url, string timeZone)
    {
        var lines = new List<string> { $"{coach} saved a coaching update for you.", "" };
        if (AgreementChanged) lines.Add("Your agreement was created or changed. Review the current terms and approve them if requested.");
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(timeZone); } catch { zone = TimeZoneInfo.Utc; }
        foreach (var workout in Workouts.Take(10))
        {
            var deadline = workout.DeadlineUtc is { } due ? $"; due {TimeZoneInfo.ConvertTime(due, zone):MMM d, yyyy h:mm tt} ({timeZone})" : "";
            lines.Add($"Snap Workout: {workout.Name} · {workout.Date}{deadline}");
        }
        if (Workouts.Count > 10) lines.Add("More assignments are available in your account.");
        if (RemovedWorkouts) lines.Add("Your coach also removed one or more assignments. Review the current list.");
        lines.AddRange(["", "Open your secure 12WR account to review the update:", url, "", "Manage email and phone alerts in your account's Notifications page."]);
        return string.Join(Environment.NewLine, lines);
    }
}
