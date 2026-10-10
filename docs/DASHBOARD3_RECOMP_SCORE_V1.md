# Dashboard3: Recomp adherence cockpit (TEST)

Routes: `/Dashboard3`, `/Dashboard/Dashboard3`. Original Dashboard and Dashboard2 remain intact.

The 0–100 score estimates adherence to a saved weekly strategy, not readiness, fat loss,
muscle gain, or logging frequency. Monday begins with full *potential*. Known past-day
macro misses and today's known overages reduce that potential. Unknown intake is not zero.
The completed-day evidence denominator is always displayed separately.

Version `RECOMP_SCORE_V2` (prior V1 snapshots retained): protein 25, carbs 20, fat 10, glycogen work plus resistance 25, cardio/mobility 8,
recovery/Zone 2 7, flexibility 5. The weights and food-count thresholds are product policy,
not scientifically validated coefficients. The existing Dashboard2 target bands are reused.
Daily macro quality is 1 within range; below range is actual/lower bound; above range is
upper bound/actual. Today's shortfalls and future days retain potential. Daily quality is
averaged over the active days of this calendar week. Colors: <60 red, 60–79 yellow, >=80 green.

Training quality is min(1, (completed + remaining scheduled opportunities)/weekly target).
Session rows are the canonical completion boundary. One session belongs to one category;
mixed gym belongs to resistance, Orangetheory 2G to conditioning, mobility to cardio/mobility,
and Zone 2 to recovery. Explicit rest completion counts; a past planned rest day may also
count with a check-in and no hard session. Extra sessions add no points. Captured weekly
commitments cannot be reduced by deleting a missed planned row. No punishment workout or
make-up feeding is prescribed.

Flexible items are explicitly reported for each day with CSRF-protected POST. Food names
are not heuristically classified. A count can be corrected by submitting its replacement.
0–2 reported items are green, 3–4 yellow, >=5 red; a reported flexible-item day with any known
macro above its band is red. This flags correlation with combined intake, not causation by a
particular food. Unknown counts are displayed as unconfirmed and do not count as known zero.

The combined energy bar is secondary. Its green display range is ±5% of the daily central
energy prescription, an explicit display tolerance. The allocator continues to honor the
central energy target rather than suggesting food up to the display ceiling.

## Persistence and future circular analysis

Uses existing `app.AnalysisRuns` storage: `RECOMP_SCORE_V2` for new versioned score snapshots,
`RECOMP_FLEX_V1` for reported daily counts. No schema migration or runtime DDL. All reads
and writes are scoped to the active client's program, not arbitrary posted client IDs.
Snapshots contain rubric version, week, inputs, component points, evidence coverage and
training commitments. Unchanged current inputs do not create another score snapshot.
Earlier captures are retained when entries or plans change. A storage error leaves the
current view available with a visible notice; it does not pretend history was saved.

First same-day target captures are retained for later historical nutrition assessment.
Days predating captures use reconstructed prescriptions and are labeled accordingly.
Daily displayed history is the latest saved check for that date; an unobserved Monday score
is not fabricated. Body measurements remain separate. Score/results comparison must retain
evidence completeness, prescription provenance, version and whether a week was finalized.
High projected scores based on unknown days are not strong adherence evidence.

The clickable preferred coach explains component points, daily evidence and useful next
actions. Tomorrow, canonical daily training, analysis and advancing learning remain below
the cockpit. Build/test precede Azure TEST deployment; deployment checks verify static
assets and the protected route without introducing an authentication bypass.

## Compact gauge grid, V2

Top row: Recomp, Nutrition, Workout. Second row: Protein, Carbs, Fat. Combined energy
is a compact clickable bar beneath. Bright red #ff2438, yellow #ffe600, green #39ff14.
Every gauge is a keyboard-accessible button opening a shared preferred-trainer dialog.
Only the relevant panel is visible, with score history, flexible-item entry, meal planning,
weekly training counts and daily evidence available inside the appropriate popup.

Nutrition normalizes the three macro components (55 points) to 100. Workout normalizes
work/resistance, mobility and recovery (40 points) to 100. Recomp retains flexibility's
5 points and combines all components. V2's 25-point work/resistance fraction is the lower
of resistance session potential and modeled weekly glycogen-work potential. Glycogen
potential = min(1, (completed modeled demand + remaining scheduled demand)/weekly target).
Engine 3.0's canonical actual-workout projection supplies completed demand; its weekly
planning projection supplies planned demand. Captured weekly burn commitments cannot be
reduced by deleting plans. No bonus for extra burn, and extra cardio cannot replace missed
resistance. Modeled burn is an estimate rather than a physiological measurement. Missing
or zero burn goals are neutral and plan limitations are explained in the trainer briefing.
V2 snapshots include modeled actual, target and remaining burn; earlier V1 history remains
versioned rather than being rewritten to the new rubric.

## Workout emphasis and two-day preview

The compact cockpit is followed by prominent Planned and Actual daily training cards,
next-day and following-day workout previews, Coaches Corner, individual daily macro
cards, and collapsed detailed daily/four-day analysis. Previews read the two explicit
future dates from the active client's saved program, including across Sunday/Monday.
Client-owned template names are preserved; sample links use the corresponding System
Workout recipe and date. Viewing a sample does not accept or alter the saved plan.
Unscheduled days link to that day's planning week instead of inventing a workout.
Training, recovery and macro sections use brighter accents with reduced-motion support.
