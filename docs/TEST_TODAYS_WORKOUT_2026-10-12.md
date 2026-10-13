# TEST: Today’s Workout and daily-record separation

Scope: TEST branch only. Recomp Analysis remains a subsequent module.

## User flow

- Daily Entries is the read-first food/completed-workout ledger; corrections remain available.
- Scale & Feel owns measurements, recovery and front/back body-region inputs. Fitdays sync-on-open and manual pull move here.
- Today’s Workout combines a current body check, recent training, existing Engine 3 fuel estimates and user intent. It previews System Workouts, My Workouts or a targeted assembly. Accept creates an editable draft; only completion supplies actual training.
- Body check records soreness and pain independently, side, movement limitation and sharp/worsening pain. A no-symptoms button handles the common case quickly.
- The weekly plan remains the original comparison. Completed sessions are overlaid and drive nutrition, fuel/energy metrics and weekly assessment. Edits/deletes through Workouts are reflected on the next read.
- Past days and days with completed actuals cannot be edited through the weekly planner. Server-side guards apply to ordinary Save, recommended-week application and custom-workout assignment. Past workout changes through Daily Entries are ignored; use Workouts.

## Data and implementation

Regional information uses a versioned compact envelope in the existing DailyCheckins.notes column (2,000 characters). Legacy prose remains readable and the legacy notes editor preserves the regional envelope. No database migration, new credential or SQL permissions are introduced. Existing measurement import/manual-correction behavior is retained. Nutrition saves no longer write measurements/check-ins.

Recommendations are computed on the server from owned client/program data. Accept revalidates the recommendation revision, rejecting changed previews. Draft creation is transactional and reuses an identical open draft. Source templates and system recipes are never edited.

## Interpretation limits

These are explicitly recommendation heuristics, not clinical clearance or validated muscle-recovery percentages:

- Any reported regional pain, sharp/worsening pain, limited movement or soreness >=8 excludes loading that region.
- Soreness >=4 or sufficiently recent hard-set exposure reduces working sets and effort. Recent load is a seven-day, day-decayed ranking indicator; category-only records use a coarse estimate and are disclosed.
- Joint loading uses broad dependencies and named squat/deadlift/back-loading checks. Left/right symptoms conservatively exclude the whole exercise. Exercise metadata cannot establish that every movement is safe.
- Existing fuel-readiness bands, very low energy or short sleep cap hard-session effort at moderate. Missing carb logs are described as uncertainty, not zero intake.
- Exercise-based targeted assembly prioritizes library matches, equipment and coverage. Missing targets/add-ons are disclosed. The exercise library still limits balance/mobility coverage. Dose/time allocation is a planning heuristic; actual session inputs remain authoritative.
- Body-composition and macro formulas are unchanged. Existing completed-workout actualization is extended to displayed energy/fuel metrics and workout labels.

## Validation

Release build, full automated suite and new policy/in-memory integration cases cover regional exclusions, equipment, recent hard sets, non-mutating previews, body-check completion, storage compatibility, nutrition/check-in isolation and past actual protection. Normal TEST workflow builds/tests/publishes/deploys and verifies health, native sign-in and new protected routes/assets. Signed-in visual verification requires a normal authenticated browser session.
