# 12WR TEST v1009 - Client Experience

## Purpose

Make the first-time client experience explain the actual 12 Week Recomp
workflow before the client begins entering data.

## Public pages

The existing Welcome and Evidence pages remain the public marketing /
credibility surfaces. v1009 preserves their approved content while including
them in the UTF-8 / mojibake audit.

The procedural education is concentrated at the first authenticated setup
surface so the client sees it when it becomes actionable.

## First-week loop

Plan -> Do -> Log -> Measure -> Learn -> Adapt

- Weekly Plan = intention.
- Daily Entry = what actually happened.
- Morning scale = next-morning response evidence.
- Dashboard = interpretation / coaching.
- Progress = trend.

## Training-day context

Hard training:
- protein protected;
- useful glycogen restoration prioritized;
- modest calorie undershoot can still be productive.

Easy cardio / recovery:
- energy control receives more weight;
- lower carbohydrate is not automatically a problem.

## Scale

One morning does not prove fat gain, fat loss, or muscle gain.

Glycogen, associated water, sodium, hydration, training inflammation, and
food/gut content can all move body weight quickly.

## Logging

The application can only interpret the evidence entered.

Common sources of nutrition under-tracking include:
- portions;
- cooking oils;
- sauces / condiments;
- restaurant estimates;
- snacks;
- beverages;
- omitted entries.

## First-sign-in welcome email

When a real authenticated user is created for the first time:
1. UserAccount is saved first.
2. The application attempts one welcome email through the existing
   IEmailService / MicrosoftGraphEmailService path.
3. Mail failure never blocks sign-in or user persistence.
4. Existing users are not emailed retroactively.
5. development-user and demo:* identities are skipped.

No database schema change is introduced by this implementation.

## Email message posture

The welcome message describes 12WR as a 12-week learning process.

It says the underlying methods are evidence-based and have a strong track
record when applied consistently, while explicitly stating that individual
results vary. It does not claim that 12WR guarantees a particular outcome.

## Encoding standard

All source modified by this patch is explicitly read as UTF-8 and written as
UTF-8 without BOM.

Common mojibake patterns are repaired in active source. Razor display
separators should prefer HTML entities such as &middot; when practical so a
future Windows PowerShell read/write cycle cannot turn them into visible
double-encoded punctuation.
## v1009 Learning Journey Rewrite

The first-login How It Works page and first-sign-in email now share the same
core teaching model:

1. 12 Week Recomp is a learning program, not merely a tracking application.
2. Purposeful daily movement is expected; resistance training is the primary
   muscle-building / muscle-preserving training signal.
3. Planned and actual activity feed the individualized daily energy and
   macronutrient prescription.
4. More demanding planned activity can appropriately require more fuel,
   especially carbohydrate for hard resistance / conditioning work.
5. The user selects the primary recomp emphasis: fat loss, balanced recomp, or
   muscle gain.
6. Weekly Plan establishes realistic intended activity.
7. Morning scale measurements provide response evidence; body weight alone is
   sufficient when richer BIA data is unavailable.
8. Daily Entry records what activity actually happened.
9. External food tracking can be used throughout the day; daily calorie,
   protein, carbohydrate, and fat totals are entered into 12WR.
10. Complete data improves analysis, but the engine should make the best
    prescription and interpretation possible from available evidence.
11. Consistency across a meaningful portion of the 12 weeks is more important
    than perfection on any single day.
12. The intended outcome includes learning repeatable training and nutrition
    habits. At the end of a term, the user may continue for another term or
    graduate from active use with stronger self-management skills.

Outcome language remains appropriately qualified: individual results vary and
the application does not guarantee a specific physique, health outcome, or
lifespan.