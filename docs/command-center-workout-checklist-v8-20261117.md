# Command Center workout checklist — V8

LIVE implementation replaces workload-weighted training credit with six explicit requirements in the seven-day window ending on the displayed day: upper push, upper pull, legs, two cardio sessions, and one confirmed rest day. Completed workouts today count immediately. Nutrition continues to use the previous seven completed days; both date windows are displayed.

Each unmet requirement deducts exactly five points. Workout = 100 − deductions (70–100). Overall Recomp = the known nutrition/flexibility base − the same workout deductions, floored at zero; when that base is unknown, the overall gauge shows the workout score alone. Nutrition's existing macro-range calculation is unchanged. No additional points are awarded for extra training or modeled calorie/glycogen burn. New programs build seven days of baseline before workout deductions start.

Initial configurable product tracking thresholds are 60 working reps per muscle group, or a completed matching strength session of at least 20 minutes when no detailed reps exist; cardio sessions require at least 20 minutes. Settings: CommandCenterTraining:MinimumGroupReps and CommandCenterTraining:MinimumSessionMinutes. These are transparent scoring defaults, not validated medical or physiological thresholds and not a replacement for the client's training prescription.

Actual working reps accumulate across completed sessions, exclude warm-ups, and use recognized movements or primary muscle mapping. Detailed exercise rows do not receive generic session-type fallback. Snap Workouts inspect their actual exercise results rather than claiming full-body coverage from their generic engine code; duplicate detailed/Snap representations are not double counted. Unmapped exercises are reported for review.

Completed full-body Strength 50 class logs can satisfy all three strength categories. Upper/lower variants and known split recipes credit only their coverage. Standard Orangetheory 2G/3G, HIIT, Zone 2, and other supported cardio modes count as cardio sessions. Two genuine sessions may occur on the same date; duplicate session IDs are counted once. A scheduled future session does not count.

Rest requires an explicit completed rest log, or a check-in on a finished planned rest date, with no strength/cardio activity on that date. Today cannot receive rest credit from a plan alone. Mobility can coexist with rest; a missing log alone is not rest. Positive recorded reps prevent contradictory rest credit even when duration is missing.

The Command Center now includes a six-card checklist with covered/missing evidence. The Workout dial's color distinguishes complete coverage from unmet requirements even at a high numerical score. Clicking the dial or any checklist card opens the exact missing reps/session/rest actions. The overall score explanation displays the nutrition/flexibility base and the workout subtraction. Missing sessions should be handled in a sensible weekly rotation, not crammed into one day to change the score.

V8 snapshots retain the checklist with their saved score. Previously captured V1–V7 scores, components, dates, and nutritional prescriptions retain their original meaning. No Fitdays measurement, workout record, coach agreement, financial assessment, or fee calculation is rewritten. Camera functionality remains on hold.

Verification includes scoring and database-evidence tests, historical snapshot restoration, responsive checklist/dialog JavaScript checks, and desktop/mobile browser checks in the LIVE deployment workflow.
