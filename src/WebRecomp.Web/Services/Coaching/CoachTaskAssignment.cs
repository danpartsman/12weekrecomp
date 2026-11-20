namespace WebRecomp.Web.Services.Coaching;

public sealed record CoachTaskAssignment(
    Guid Id, Guid CoachId, Guid OwnerId, Guid RelationshipId, string Title, string? Notes, string Billing,
    string Unit, decimal Minimum, decimal Maximum, decimal Rate, decimal LateFee, DateTimeOffset DueUtc,
    DateTimeOffset AssignedUtc)
{
    public int Version { get; init; } = 1;
    public DateTimeOffset? FirstStartedUtc { get; init; }
    public DateTimeOffset? RunningSinceUtc { get; init; }
    public DateTimeOffset? LastStoppedUtc { get; init; }
    public DateTimeOffset? CompletedUtc { get; init; }
    public decimal? Quantity { get; init; }
    public long ElapsedTicks { get; init; }
    public decimal WorkFeePosted { get; init; }
    public bool LateFeePosted { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsTimed => Billing is "minute" or "hour";

    public long ActiveTicks(DateTimeOffset now)
    {
        var ticks = ElapsedTicks + (RunningSinceUtc is { } start ? Math.Max(0, (now - start).Ticks) : 0);
        return IsTimed ? Math.Min(ticks, (long)(Maximum * TimeSpan.TicksPerMinute)) : ticks;
    }

    public decimal WorkFee(DateTimeOffset now)
    {
        var billable = Billing switch
        {
            "minute" => ActiveTicks(now) / (decimal)TimeSpan.TicksPerMinute,
            "hour" => ActiveTicks(now) / (decimal)TimeSpan.TicksPerHour,
            "quantity" => Quantity ?? 0,
            _ => CompletedUtc.HasValue ? 1 : 0
        };
        return AccountabilityAssessment.Cents(billable * Rate);
    }
}
