Web Recomp v1002 — Option 4 Dashboard Redesign

This patch supersedes the earlier v1002_dashboard_integrity_patch. Do NOT install both.

NO DATABASE MIGRATION IS REQUIRED.
NO CALCULATION ENGINE FORMULAS ARE CHANGED.
Engine remains 1001.0 and configuration remains CFG-1001.0.

WHAT THIS PATCH CHANGES

1. Dashboard becomes an operational Today-only screen.
   - Removes historical Dashboard date browsing.
   - Historical corrections remain on Weigh In Stats, Workout Actual, and Nutrition.

2. TODAY uses the structured Option 4 layout.
   - Workout: planned session, focus, description, estimated duration, effort range, and Recomp keep/change decision.
   - Nutrition: calories/macros, day classification, and a deterministic rule-based coaching tip.
   - Coach: uses current recommendation plus energy, soreness, sleep, pain/injury, illness/systemic fatigue, and glycogen readiness.
   - Safety flags outrank performance/fueling advice.

3. TOMORROW'S PLAN
   - Shows tomorrow's planned workout and description.
   - Shows a preliminary base nutrition estimate using what Recomp knows today.
   - Clearly labels the values Preliminary; tomorrow's check-in can change the final recommendation.

4. FUEL & GLYCOGEN REFILL
   - Shows yesterday's nutrition target vs actual.
   - Shows carbohydrate difference.
   - Shows yesterday's actual workout glycogen demand and restoration credit using the SAME restoration-credit formula as RecommendationService.
   - Shows how that flows into the current four-day demand, restoration credit, recovery %, functional target, target achievement, recovery allocation, and pre-workout carb allocation.
   - Explains that carbohydrate is the most direct dietary route for replenishing muscle glycogen, while gluconeogenesis can contribute glucose from lactate, glycerol, and glucogenic amino acids.

5. Workout Plan historical integrity
   - Each displayed planned date uses the latest known weight on or before that date.
   - A later weigh-in no longer changes historical displayed macro targets.

INTENTIONALLY DEFERRED
- Favorite treats: no DB change yet; high-carb tips use generic "planned higher-carb treat" language.
- Exact exercise/set prescription: workout types currently store duration/focus, not detailed exercise sets.
- Duplicate AnalysisRuns caused by Dashboard reads: confirmed issue remains deferred to v1004 centralized recalculation. The Dashboard is now Today-only, but it still invokes the existing v1001 calculation/storage pipeline.
- Long-term weight/body-fat/muscle/waist charts: reserved for the later History/Trends dashboard.

INSTALL
Extract this ZIP into:
C:\RecompAzure\web_recomp_v1002
and overwrite existing files.

CHECKPOINT 1 — BUILD ONLY
cd C:\RecompAzure\web_recomp_v1002\src\WebRecomp.Web
dotnet build -c Release

STOP after the build and review the result before regression or deployment.
- Measurement trend aggregation: the operational Dashboard no longer calculates 7-day body-composition trends. When History/Trends is implemented, aggregate one authoritative measurement per calendar date (latest same-day row wins) while preserving raw measurement rows.
