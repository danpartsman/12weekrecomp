# v1001 regression tests

`v1001_regression_vectors.json` is the acceptance contract for the schedule-first change.

The most important test is `TC-001-DAN-V64-BASELINE`.

The web engine is not considered behaviorally compatible until the same Dan inputs reproduce approximately:

- Major Push
- KEEP PLAN
- GOOD weekly structure
- 70% absolute four-day restoration
- 75% functional target
- 94% target achievement
- Slightly reduced readiness
- 10 g carryover allocation
- 10 g pre-workout carryover
- 2,150 kcal
- 190 g protein
- 190 g carbohydrate
- 70 g fat

Tolerance for displayed ratios: ±0.5 percentage point.
Macro tolerance: exact after configured rounding.

Other mandatory tests protect the design decision that:
- yesterday's recovery day does not imply Zone 2 today,
- hard-session count is a warning rather than an automatic downgrade,
- an unrecovered heavy region and very-low fuel readiness remain valid overrides,
- Zone 2 is recovery cardio rather than primary conditioning.
